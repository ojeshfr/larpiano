using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Keynote.App.Views.Auth;
using Keynote.Infrastructure;

namespace Keynote.App;

public partial class MainWindow
{
    private readonly IAuthApiClient authApiClient = new AuthApiClient();
    private readonly IAuthTokenStore authTokenStore = new AuthTokenStore();
    private AuthUserProfile? authenticatedUser;
    private string? currentAccessToken;
    private string? currentRefreshToken;
    private PresenceReportingService? presenceService;

    private void InitializeAuth()
    {
        presenceService = new PresenceReportingService();
        _ = presenceService.StartAsync();

        // Asynchronously check stored DPAPI credentials
        _ = Task.Run(async () =>
        {
            try
            {
                var stored = await authTokenStore.LoadTokensAsync().ConfigureAwait(false);
                if (stored != null)
                {
                    currentAccessToken = stored.AccessToken;
                    currentRefreshToken = stored.RefreshToken;

                    // Verify token with backend
                    var profile = await authApiClient.GetProfileAsync(stored.AccessToken).ConfigureAwait(false);
                    if (profile == null && !string.IsNullOrWhiteSpace(stored.RefreshToken))
                    {
                        // Access token may be expired; try rotating
                        var refreshed = await authApiClient.RefreshAsync(stored.RefreshToken).ConfigureAwait(false);
                        if (refreshed.Success && refreshed.Tokens != null)
                        {
                            currentAccessToken = refreshed.Tokens.AccessToken;
                            currentRefreshToken = refreshed.Tokens.RefreshToken;
                            await authTokenStore.SaveTokensAsync(refreshed.Tokens.AccessToken, refreshed.Tokens.RefreshToken).ConfigureAwait(false);
                            profile = refreshed.User ?? await authApiClient.GetProfileAsync(currentAccessToken).ConfigureAwait(false);
                        }
                    }

                    if (profile != null)
                    {
                        authenticatedUser = profile;
                        if (presenceService != null)
                        {
                            presenceService.IsConsentGranted = profile.PresenceConsent;
                        }

                        await Dispatcher.InvokeAsync(() => UpdateUserAccountHeader(profile));
                    }
                }
            }
            catch (Exception)
            {
                // Resilient fallback: network error or offline mode should leave Guest mode active
            }
        });
    }

    private void UpdateUserAccountHeader(AuthUserProfile? user)
    {
        if (user != null)
        {
            HeaderUserName.Text = user.DisplayName;
            HeaderUserInitial.Text = !string.IsNullOrEmpty(user.DisplayName)
                ? user.DisplayName[..1].ToUpperInvariant()
                : "U";
            HeaderAccountButton.ToolTip = $"{user.DisplayName} ({user.Email}) - Click for Account Settings";
        }
        else
        {
            HeaderUserName.Text = "Guest";
            HeaderUserInitial.Text = "G";
            HeaderAccountButton.ToolTip = "Account & Security (Guest Mode)";
        }
    }

    private void HeaderAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var authWindow = new AuthWindow(
            api: authApiClient,
            tokenStore: authTokenStore,
            presence: presenceService,
            existingUser: authenticatedUser,
            existingAccessToken: currentAccessToken,
            existingRefreshToken: currentRefreshToken)
        {
            Owner = this
        };

        authWindow.OnAuthenticated += user =>
        {
            authenticatedUser = user;
            if (presenceService != null)
            {
                presenceService.IsConsentGranted = user.PresenceConsent;
            }
            UpdateUserAccountHeader(user);
        };

        authWindow.OnLoggedOut += () =>
        {
            authenticatedUser = null;
            currentAccessToken = null;
            currentRefreshToken = null;
            if (presenceService != null)
            {
                presenceService.IsConsentGranted = false;
            }
            UpdateUserAccountHeader(null);
        };

        authWindow.ShowDialog();
    }

    private async Task DisposeAuthAsync()
    {
        if (presenceService != null)
        {
            await presenceService.DisposeAsync();
        }
    }
}
