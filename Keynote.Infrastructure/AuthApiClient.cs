using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Keynote.Infrastructure;

public sealed record AuthUserProfile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("email_verified")] bool EmailVerified,
    [property: JsonPropertyName("presence_consent")] bool PresenceConsent = false,
    [property: JsonPropertyName("telemetry_consent")] bool TelemetryConsent = false
);

public sealed record AuthTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn
);

public sealed record AuthResult(
    bool Success,
    string Message,
    AuthUserProfile? User = null,
    AuthTokens? Tokens = null,
    int? Cooldown = null
);

public interface IAuthApiClient
{
    Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default);
    Task<AuthResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default);
    Task<AuthResult> ResendCodeAsync(string email, CancellationToken cancellationToken = default);
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<bool> LogoutAllAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<AuthResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<AuthResult> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default);
    Task<AuthUserProfile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<bool> UpdatePreferencesAsync(string accessToken, bool presenceConsent, bool telemetryConsent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Client service for communicating with the Larpiano Authentication backend.
/// </summary>
public sealed class AuthApiClient : IAuthApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public AuthApiClient(HttpClient? httpClient = null, string? baseUrl = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("LARPIANO_API_URL") ?? "https://larpiano.portways.app").TrimEnd('/');
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email, password, display_name = displayName };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/register", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Registration failed.");

            return new AuthResult(success, message);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not reach authentication server: " + ex.Message);
        }
    }

    public async Task<AuthResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email, code };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/verify-email", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Verification failed.");

            if (!success)
            {
                return new AuthResult(false, message);
            }

            AuthUserProfile? user = null;
            AuthTokens? tokens = null;

            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("user", out var u))
                {
                    user = JsonSerializer.Deserialize<AuthUserProfile>(u.GetRawText());
                }
                if (data.TryGetProperty("tokens", out var t))
                {
                    tokens = JsonSerializer.Deserialize<AuthTokens>(t.GetRawText());
                }
            }

            return new AuthResult(true, message, user, tokens);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not connect to verify email: " + ex.Message);
        }
    }

    public async Task<AuthResult> ResendCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/resend-code", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Resend failed.");
            int? cooldown = root.TryGetProperty("cooldown", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;

            return new AuthResult(success, message, Cooldown: cooldown);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not connect to server: " + ex.Message);
        }
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email, password };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/login", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Login failed.");

            if (!success)
            {
                return new AuthResult(false, message);
            }

            AuthUserProfile? user = null;
            AuthTokens? tokens = null;

            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("user", out var u))
                {
                    user = JsonSerializer.Deserialize<AuthUserProfile>(u.GetRawText());
                }
                if (data.TryGetProperty("tokens", out var t))
                {
                    tokens = JsonSerializer.Deserialize<AuthTokens>(t.GetRawText());
                }
            }

            return new AuthResult(true, message, user, tokens);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not connect to authentication server: " + ex.Message);
        }
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { refresh_token = refreshToken };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/refresh", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Refresh failed.");

            if (!success)
            {
                return new AuthResult(false, message);
            }

            AuthUserProfile? user = null;
            AuthTokens? tokens = null;

            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("user", out var u))
                {
                    user = JsonSerializer.Deserialize<AuthUserProfile>(u.GetRawText());
                }
                tokens = JsonSerializer.Deserialize<AuthTokens>(data.GetRawText());
            }

            return new AuthResult(true, message, user, tokens);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not refresh session: " + ex.Message);
        }
    }

    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { refresh_token = refreshToken };
            await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/logout", payload, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LogoutAllAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/logout-all");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AuthResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/forgot-password", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "Password reset dispatched.";
            return new AuthResult(true, message);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not request password recovery: " + ex.Message);
        }
    }

    public async Task<AuthResult> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email, code, new_password = newPassword };
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/reset-password", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Password reset failed.");

            return new AuthResult(success, message);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not reset password: " + ex.Message);
        }
    }

    public async Task<AuthUserProfile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/auth/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("user", out var u))
            {
                return JsonSerializer.Deserialize<AuthUserProfile>(u.GetRawText());
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdatePreferencesAsync(string accessToken, bool presenceConsent, bool telemetryConsent, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/preferences");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = JsonContent.Create(new { presence_consent = presenceConsent, telemetry_consent = telemetryConsent });
            var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
