using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Keynote.Infrastructure;

namespace Keynote.App.Views.Auth;

public partial class AuthWindow : Window
{
    private readonly IAuthApiClient _api;
    private readonly IAuthTokenStore _tokenStore;
    private readonly IPresenceReportingService? _presence;

    private AuthUserProfile? _currentUser;
    private string? _currentAccessToken;
    private string? _currentRefreshToken;

    private DispatcherTimer? _resendTimer;
    private int _resendSecondsRemaining;
    private string _pendingVerificationEmail = "";

    public AuthUserProfile? CurrentUser => _currentUser;
    public string? CurrentAccessToken => _currentAccessToken;
    public string? CurrentRefreshToken => _currentRefreshToken;
    public bool IsAuthenticated => _currentUser != null;

    public event Action<AuthUserProfile>? OnAuthenticated;
    public event Action? OnLoggedOut;
    public event Action? OnGuestModeSelected;

    public AuthWindow(
        IAuthApiClient? api = null,
        IAuthTokenStore? tokenStore = null,
        IPresenceReportingService? presence = null,
        AuthUserProfile? existingUser = null,
        string? existingAccessToken = null,
        string? existingRefreshToken = null)
    {
        InitializeComponent();
        _api = api ?? new AuthApiClient();
        _tokenStore = tokenStore ?? new AuthTokenStore();
        _presence = presence;

        _currentUser = existingUser;
        _currentAccessToken = existingAccessToken;
        _currentRefreshToken = existingRefreshToken;

        if (_currentUser != null)
        {
            ShowProfileView();
        }
        else
        {
            ShowWelcomeView();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _resendTimer?.Stop();
        _resendTimer = null;
        base.OnClosed(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HideAllPanels()
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Collapsed;
        RegisterPanel.Visibility = Visibility.Collapsed;
        VerifyEmailPanel.Visibility = Visibility.Collapsed;
        ResetPasswordPanel.Visibility = Visibility.Collapsed;
        ProfilePanel.Visibility = Visibility.Collapsed;
    }

    public void ShowWelcomeView()
    {
        HideAllPanels();
        WelcomePanel.Visibility = Visibility.Visible;
    }

    public void ShowSignInView()
    {
        HideAllPanels();
        SignInErrorText.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Visible;
    }

    public void ShowRegisterView()
    {
        HideAllPanels();
        RegisterErrorText.Visibility = Visibility.Collapsed;
        RegisterPanel.Visibility = Visibility.Visible;
    }

    public void ShowVerificationView(string email)
    {
        HideAllPanels();
        _pendingVerificationEmail = email;
        VerifyEmailSubtitle.Text = $"Enter the 6-digit code dispatched to {email}.";
        VerifyErrorText.Visibility = Visibility.Collapsed;
        VerifySuccessText.Visibility = Visibility.Collapsed;
        VerifyCodeText.Text = "";
        StartResendCooldown(60);
        VerifyEmailPanel.Visibility = Visibility.Visible;
    }

    public void ShowResetPasswordView()
    {
        HideAllPanels();
        ResetErrorText.Visibility = Visibility.Collapsed;
        ResetEmailText.Text = SignInEmailText.Text.Trim();
        ResetPasswordPanel.Visibility = Visibility.Visible;
    }

    public void ShowProfileView()
    {
        if (_currentUser == null)
        {
            ShowSignInView();
            return;
        }

        HideAllPanels();
        ProfileDisplayName.Text = _currentUser.DisplayName;
        ProfileEmail.Text = _currentUser.Email;
        ProfileInitial.Text = !string.IsNullOrEmpty(_currentUser.DisplayName)
            ? _currentUser.DisplayName[..1].ToUpperInvariant()
            : "P";
        ProfileRole.Text = _currentUser.Role == "admin" ? "Administrator" : "Pianist";
        ProfileVerifiedBadge.Text = _currentUser.EmailVerified ? "Verified ✓" : "Unverified ⚠";

        ProfilePresenceConsent.IsChecked = _currentUser.PresenceConsent;
        ProfileTelemetryConsent.IsChecked = _currentUser.TelemetryConsent;

        ProfilePanel.Visibility = Visibility.Visible;
    }

    private void ShowWelcome_Click(object sender, RoutedEventArgs e) => ShowWelcomeView();
    private void ShowSignIn_Click(object sender, RoutedEventArgs e) => ShowSignInView();
    private void ShowRegister_Click(object sender, RoutedEventArgs e) => ShowRegisterView();
    private void ShowForgotPassword_Click(object sender, MouseButtonEventArgs e) => ShowResetPasswordView();

    private void ContinueAsGuest_Click(object sender, RoutedEventArgs e)
    {
        OnGuestModeSelected?.Invoke();
        Close();
    }

    private void ToggleSignInPassword_Click(object sender, RoutedEventArgs e)
    {
        if (SignInPasswordVisible.Visibility == Visibility.Visible)
        {
            SignInPasswordBox.Password = SignInPasswordVisible.Text;
            SignInPasswordVisible.Visibility = Visibility.Collapsed;
            SignInPasswordBox.Visibility = Visibility.Visible;
        }
        else
        {
            SignInPasswordVisible.Text = SignInPasswordBox.Password;
            SignInPasswordBox.Visibility = Visibility.Collapsed;
            SignInPasswordVisible.Visibility = Visibility.Visible;
        }
    }

    private async void SignInSubmit_Click(object sender, RoutedEventArgs e)
    {
        var email = SignInEmailText.Text.Trim();
        var password = SignInPasswordVisible.Visibility == Visibility.Visible
            ? SignInPasswordVisible.Text
            : SignInPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowSignInError("Please enter your email and password.");
            return;
        }

        SignInSubmitButton.IsEnabled = false;
        SignInSubmitButton.Content = "Signing In…";
        SignInErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _api.LoginAsync(email, password).ConfigureAwait(true);
            if (!res.Success || res.User == null || res.Tokens == null)
            {
                ShowSignInError(res.Message);
                return;
            }

            _currentUser = res.User;
            _currentAccessToken = res.Tokens.AccessToken;
            _currentRefreshToken = res.Tokens.RefreshToken;

            if (SignInRememberMe.IsChecked == true)
            {
                await _tokenStore.SaveTokensAsync(res.Tokens.AccessToken, res.Tokens.RefreshToken).ConfigureAwait(true);
            }

            if (_presence != null)
            {
                _presence.IsConsentGranted = res.User.PresenceConsent;
            }

            OnAuthenticated?.Invoke(_currentUser);
            ShowProfileView();
        }
        catch (Exception ex)
        {
            ShowSignInError("Sign in failed: " + ex.Message);
        }
        finally
        {
            SignInSubmitButton.IsEnabled = true;
            SignInSubmitButton.Content = "Sign In";
        }
    }

    private void ShowSignInError(string error)
    {
        SignInErrorText.Text = error;
        SignInErrorText.Visibility = Visibility.Visible;
    }

    private async void RegisterSubmit_Click(object sender, RoutedEventArgs e)
    {
        var name = RegisterDisplayNameText.Text.Trim();
        var email = RegisterEmailText.Text.Trim();
        var pwd = RegisterPasswordBox.Password;
        var confirm = RegisterConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pwd))
        {
            ShowRegisterError("Please fill in all required fields.");
            return;
        }

        if (pwd != confirm)
        {
            ShowRegisterError("Passwords do not match.");
            return;
        }

        RegisterSubmitButton.IsEnabled = false;
        RegisterSubmitButton.Content = "Creating Account…";
        RegisterErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _api.RegisterAsync(email, pwd, name).ConfigureAwait(true);
            if (!res.Success)
            {
                ShowRegisterError(res.Message);
                return;
            }

            ShowVerificationView(email);
        }
        catch (Exception ex)
        {
            ShowRegisterError("Registration failed: " + ex.Message);
        }
        finally
        {
            RegisterSubmitButton.IsEnabled = true;
            RegisterSubmitButton.Content = "Create Account";
        }
    }

    private void ShowRegisterError(string error)
    {
        RegisterErrorText.Text = error;
        RegisterErrorText.Visibility = Visibility.Visible;
    }

    private async void VerifySubmit_Click(object sender, RoutedEventArgs e)
    {
        var code = VerifyCodeText.Text.Trim();
        if (code.Length != 6)
        {
            VerifyErrorText.Text = "Please enter the full 6-digit code.";
            VerifyErrorText.Visibility = Visibility.Visible;
            return;
        }

        VerifySubmitButton.IsEnabled = false;
        VerifySubmitButton.Content = "Verifying…";
        VerifyErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _api.VerifyEmailAsync(_pendingVerificationEmail, code).ConfigureAwait(true);
            if (!res.Success || res.User == null || res.Tokens == null)
            {
                VerifyErrorText.Text = res.Message;
                VerifyErrorText.Visibility = Visibility.Visible;
                return;
            }

            _currentUser = res.User;
            _currentAccessToken = res.Tokens.AccessToken;
            _currentRefreshToken = res.Tokens.RefreshToken;

            await _tokenStore.SaveTokensAsync(res.Tokens.AccessToken, res.Tokens.RefreshToken).ConfigureAwait(true);

            OnAuthenticated?.Invoke(_currentUser);
            ShowProfileView();
        }
        catch (Exception ex)
        {
            VerifyErrorText.Text = "Verification failed: " + ex.Message;
            VerifyErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            VerifySubmitButton.IsEnabled = true;
            VerifySubmitButton.Content = "Verify Code";
        }
    }

    private void StartResendCooldown(int seconds)
    {
        _resendSecondsRemaining = seconds;
        ResendCodeButton.IsEnabled = false;
        ResendCodeButton.Content = $"Resend Code ({_resendSecondsRemaining}s)";

        _resendTimer?.Stop();
        _resendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _resendTimer.Tick += (s, e) =>
        {
            _resendSecondsRemaining--;
            if (_resendSecondsRemaining <= 0)
            {
                _resendTimer.Stop();
                ResendCodeButton.IsEnabled = true;
                ResendCodeButton.Content = "Resend Code";
            }
            else
            {
                ResendCodeButton.Content = $"Resend Code ({_resendSecondsRemaining}s)";
            }
        };
        _resendTimer.Start();
    }

    private async void ResendCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingVerificationEmail)) return;

        ResendCodeButton.IsEnabled = false;
        try
        {
            var res = await _api.ResendCodeAsync(_pendingVerificationEmail).ConfigureAwait(true);
            if (res.Success)
            {
                VerifySuccessText.Text = "A new verification code was sent.";
                VerifySuccessText.Visibility = Visibility.Visible;
                StartResendCooldown(res.Cooldown ?? 60);
            }
            else
            {
                VerifyErrorText.Text = res.Message;
                VerifyErrorText.Visibility = Visibility.Visible;
                StartResendCooldown(res.Cooldown ?? 60);
            }
        }
        catch (Exception ex)
        {
            VerifyErrorText.Text = "Could not resend: " + ex.Message;
            VerifyErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void SendResetCode_Click(object sender, RoutedEventArgs e)
    {
        var email = ResetEmailText.Text.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            ResetErrorText.Text = "Please enter your account email.";
            ResetErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var res = await _api.RequestPasswordResetAsync(email).ConfigureAwait(true);
            ResetErrorText.Text = res.Message;
            ResetErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ResetErrorText.Text = "Error: " + ex.Message;
            ResetErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void ResetPasswordSubmit_Click(object sender, RoutedEventArgs e)
    {
        var email = ResetEmailText.Text.Trim();
        var code = ResetCodeText.Text.Trim();
        var pwd = ResetNewPasswordBox.Password;
        var confirm = ResetConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pwd))
        {
            ResetErrorText.Text = "Please complete all fields.";
            ResetErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (pwd != confirm)
        {
            ResetErrorText.Text = "Passwords do not match.";
            ResetErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var res = await _api.ResetPasswordAsync(email, code, pwd).ConfigureAwait(true);
            if (res.Success)
            {
                ShowSignInView();
                SignInEmailText.Text = email;
                ShowSignInError(res.Message); // Display success note in sign-in panel
            }
            else
            {
                ResetErrorText.Text = res.Message;
                ResetErrorText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ResetErrorText.Text = "Reset failed: " + ex.Message;
            ResetErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void OnConsentChanged(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null || string.IsNullOrWhiteSpace(_currentAccessToken)) return;

        bool presence = ProfilePresenceConsent.IsChecked == true;
        bool telemetry = ProfileTelemetryConsent.IsChecked == true;

        if (_presence != null && !presence && _presence.IsConsentGranted)
        {
            await _presence.StopAsync().ConfigureAwait(true);
            _presence.IsConsentGranted = false;
        }

        bool saved = await _api.UpdatePreferencesAsync(_currentAccessToken, presence, telemetry).ConfigureAwait(true);
        if (_presence != null && saved && presence)
        {
            _presence.IsConsentGranted = true;
            await _presence.StartAsync().ConfigureAwait(true);
        }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_presence != null)
            {
                try { await _presence.StopAsync().ConfigureAwait(true); } catch { }
            }

            var refresh = _currentRefreshToken;
            var access = _currentAccessToken;

            if (!string.IsNullOrWhiteSpace(refresh) || !string.IsNullOrWhiteSpace(access))
            {
                await _api.LogoutAsync(refresh, access).ConfigureAwait(true);
            }
        }
        catch
        {
            // Network failure or server error; local sign-out must still proceed
        }
        finally
        {
            try { await _tokenStore.ClearTokensAsync().ConfigureAwait(true); } catch { }
            _currentUser = null;
            _currentAccessToken = null;
            _currentRefreshToken = null;

            if (_presence != null)
            {
                _presence.IsConsentGranted = false;
            }

            OnLoggedOut?.Invoke();
            ShowWelcomeView();
        }
    }

    private async void SignOutAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_presence != null)
            {
                try { await _presence.StopAsync().ConfigureAwait(true); } catch { }
            }

            var access = _currentAccessToken;
            if (!string.IsNullOrWhiteSpace(access))
            {
                await _api.LogoutAllAsync(access).ConfigureAwait(true);
            }
        }
        catch
        {
            // Network failure or server error; local sign-out must still proceed
        }
        finally
        {
            try { await _tokenStore.ClearTokensAsync().ConfigureAwait(true); } catch { }
            _currentUser = null;
            _currentAccessToken = null;
            _currentRefreshToken = null;

            if (_presence != null)
            {
                _presence.IsConsentGranted = false;
            }

            OnLoggedOut?.Invoke();
            ShowWelcomeView();
        }
    }
}
