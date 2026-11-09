using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Keynote.Infrastructure;

public sealed record AuthTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn = 900
);

public sealed record AuthUserProfile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("role")] string Role = "user",
    [property: JsonPropertyName("email_verified")] bool EmailVerified = false,
    [property: JsonPropertyName("presence_consent")] bool PresenceConsent = false,
    [property: JsonPropertyName("telemetry_consent")] bool TelemetryConsent = false
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
    Task<bool> LogoutAsync(string? refreshToken, string? accessToken = null, CancellationToken cancellationToken = default);
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
    private string _baseUrl;
    private const string DefaultProductionUrl = "https://larpiano.portways.app";
    private const string LocalDevelopmentUrl = "http://127.0.0.1:8088";

    public AuthApiClient(HttpClient? httpClient = null, string? baseUrl = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("LARPIANO_API_URL") ?? DefaultProductionUrl).TrimEnd('/');
    }

    private async Task<bool> IsLocalDevRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(400));
            using var res = await _http.GetAsync($"{LocalDevelopmentUrl}/api/health", cts.Token).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> PostJsonWithFallbackAsync<T>(string endpoint, T payload, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}{endpoint}";
        HttpResponseMessage? response = null;
        try
        {
            response = await _http.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (_baseUrl == DefaultProductionUrl)
        {
            if (await IsLocalDevRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                _baseUrl = LocalDevelopmentUrl;
                return await _http.PostAsJsonAsync($"{_baseUrl}{endpoint}", payload, cancellationToken).ConfigureAwait(false);
            }
            throw;
        }

        if (response != null && (int)response.StatusCode == 501 && _baseUrl == DefaultProductionUrl)
        {
            if (await IsLocalDevRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                response.Dispose();
                _baseUrl = LocalDevelopmentUrl;
                return await _http.PostAsJsonAsync($"{_baseUrl}{endpoint}", payload, cancellationToken).ConfigureAwait(false);
            }
        }

        return response!;
    }

    private async Task<HttpResponseMessage> SendWithFallbackAsync(Func<string, HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            using var req = requestFactory(_baseUrl);
            response = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (_baseUrl == DefaultProductionUrl)
        {
            if (await IsLocalDevRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                _baseUrl = LocalDevelopmentUrl;
                using var retryReq = requestFactory(_baseUrl);
                return await _http.SendAsync(retryReq, cancellationToken).ConfigureAwait(false);
            }
            throw;
        }

        if (response != null && (int)response.StatusCode == 501 && _baseUrl == DefaultProductionUrl)
        {
            if (await IsLocalDevRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                response.Dispose();
                _baseUrl = LocalDevelopmentUrl;
                using var retryReq = requestFactory(_baseUrl);
                return await _http.SendAsync(retryReq, cancellationToken).ConfigureAwait(false);
            }
        }

        return response!;
    }

    private static (bool Success, JsonDocument? Document, string? ErrorMessage) SafeParseJson(string content, HttpResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (false, null, $"Server returned empty response (HTTP {(int)response.StatusCode}: {response.ReasonPhrase}).");
        }

        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            if ((int)response.StatusCode == 501)
            {
                return (false, null, "Authentication service is not deployed on this server (HTTP 501 Not Implemented). Please deploy the backend to Portways or start the local server.");
            }
            return (false, null, $"Authentication server returned non-JSON response (HTTP {(int)response.StatusCode}: {response.ReasonPhrase}).");
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            return (true, doc, null);
        }
        catch (JsonException)
        {
            return (false, null, $"Invalid JSON response received from authentication server (HTTP {(int)response.StatusCode}).");
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email, password, display_name = displayName };
            using var response = await PostJsonWithFallbackAsync("/api/auth/register", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Registration failed.");
            }

            using (doc)
            {
                var root = doc.RootElement;
                bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Registration failed.");
                return new AuthResult(success, message);
            }
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
            using var response = await PostJsonWithFallbackAsync("/api/auth/verify-email", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Verification failed.");
            }

            using (doc)
            {
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
            using var response = await PostJsonWithFallbackAsync("/api/auth/resend-code", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Resend failed.");
            }

            using (doc)
            {
                var root = doc.RootElement;
                bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Resend failed.");
                int? cooldown = root.TryGetProperty("cooldown", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;

                return new AuthResult(success, message, Cooldown: cooldown);
            }
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
            using var response = await PostJsonWithFallbackAsync("/api/auth/login", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Login failed.");
            }

            using (doc)
            {
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
            using var response = await PostJsonWithFallbackAsync("/api/auth/refresh", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Refresh failed.");
            }

            using (doc)
            {
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
        }
        catch (Exception ex)
        {
            return new AuthResult(false, "Could not refresh session: " + ex.Message);
        }
    }

    public async Task<bool> LogoutAsync(string? refreshToken, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await SendWithFallbackAsync(baseUrl =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/logout");
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }
                var payload = !string.IsNullOrWhiteSpace(refreshToken)
                    ? (object)new { refresh_token = refreshToken }
                    : new { };
                req.Content = JsonContent.Create(payload);
                return req;
            }, cancellationToken).ConfigureAwait(false);

            return resp.IsSuccessStatusCode;
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
            using var resp = await SendWithFallbackAsync(baseUrl =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/logout-all");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return req;
            }, cancellationToken).ConfigureAwait(false);

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
            using var response = await PostJsonWithFallbackAsync("/api/auth/forgot-password", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Password reset request failed.");
            }

            using (doc)
            {
                var root = doc.RootElement;
                string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "Password reset dispatched.";
                return new AuthResult(true, message);
            }
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
            using var response = await PostJsonWithFallbackAsync("/api/auth/reset-password", payload, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var (parsed, doc, err) = SafeParseJson(content, response);
            if (!parsed || doc == null)
            {
                return new AuthResult(false, err ?? "Password reset failed.");
            }

            using (doc)
            {
                var root = doc.RootElement;
                bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : (root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "Password reset failed.");

                return new AuthResult(success, message);
            }
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
            using var resp = await SendWithFallbackAsync(baseUrl =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/auth/me");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return req;
            }, cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var (parsed, doc, _) = SafeParseJson(content, resp);
            if (!parsed || doc == null)
            {
                return null;
            }

            using (doc)
            {
                if (doc.RootElement.TryGetProperty("user", out var u))
                {
                    return JsonSerializer.Deserialize<AuthUserProfile>(u.GetRawText());
                }
                return null;
            }
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
            using var resp = await SendWithFallbackAsync(baseUrl =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/preferences");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Content = JsonContent.Create(new { presence_consent = presenceConsent, telemetry_consent = telemetryConsent });
                return req;
            }, cancellationToken).ConfigureAwait(false);

            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
