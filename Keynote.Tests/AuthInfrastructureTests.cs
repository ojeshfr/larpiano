using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Keynote.Infrastructure;
using Xunit;

namespace Keynote.Tests;

public class AuthInfrastructureTests
{
    private sealed class MockAuthHttpHandler : HttpMessageHandler
    {
        public bool SimulateFailure { get; set; }
        public string? LastAuthHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (SimulateFailure)
            {
                throw new HttpRequestException("Network unreachable");
            }

            LastAuthHeader = request.Headers.Authorization?.ToString();
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (path.EndsWith("/api/auth/register"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true,\"message\":\"Verification code sent.\"}")
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/verify-email"))
            {
                var json = "{\"success\":true,\"message\":\"Verified.\",\"data\":{\"user\":{\"id\":\"u-101\",\"email\":\"user@example.com\",\"display_name\":\"User 101\",\"role\":\"user\",\"email_verified\":true},\"tokens\":{\"access_token\":\"jwt-access-123\",\"refresh_token\":\"opaque-refresh-456\",\"token_type\":\"Bearer\",\"expires_in\":900}}}";
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/resend-code"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true,\"message\":\"New code dispatched.\",\"cooldown\":60}")
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/login"))
            {
                var json = "{\"success\":true,\"message\":\"Login ok.\",\"data\":{\"user\":{\"id\":\"u-101\",\"email\":\"user@example.com\",\"display_name\":\"User 101\",\"role\":\"user\",\"email_verified\":true},\"tokens\":{\"access_token\":\"jwt-login-789\",\"refresh_token\":\"opaque-refresh-login-012\",\"token_type\":\"Bearer\",\"expires_in\":900}}}";
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/refresh"))
            {
                var json = "{\"success\":true,\"message\":\"Rotated.\",\"data\":{\"access_token\":\"jwt-refreshed-111\",\"refresh_token\":\"opaque-refreshed-222\",\"token_type\":\"Bearer\",\"expires_in\":900}}";
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/me"))
            {
                var json = "{\"success\":true,\"user\":{\"id\":\"u-101\",\"email\":\"user@example.com\",\"display_name\":\"User 101\",\"role\":\"user\",\"email_verified\":true,\"presence_consent\":true,\"telemetry_consent\":false}}";
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/preferences"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}")
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/auth/logout") || path.EndsWith("/api/auth/logout-all"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}")
                });
            }

            if (path.EndsWith("/api/auth/forgot-password"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true,\"message\":\"Password reset code sent.\"}")
                });
            }

            if (path.EndsWith("/api/auth/reset-password"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true,\"message\":\"Password reset successfully.\"}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task AuthTokenStore_SaveLoadAndClear_WorksWithDPAPI()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"auth_test_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);

            // Initially empty
            var initial = await store.LoadTokensAsync();
            Assert.Null(initial);

            // Save tokens
            await store.SaveTokensAsync("test-access-token-xyz", "test-refresh-token-abc");

            // Load tokens
            var loaded = await store.LoadTokensAsync();
            Assert.NotNull(loaded);
            Assert.Equal("test-access-token-xyz", loaded.AccessToken);
            Assert.Equal("test-refresh-token-abc", loaded.RefreshToken);

            // Clear tokens
            await store.ClearTokensAsync();
            var cleared = await store.LoadTokensAsync();
            Assert.Null(cleared);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task AuthApiClient_RegistrationAndVerification_ReturnsTokens()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var reg = await api.RegisterAsync("user@example.com", "Password123!", "User 101");
        Assert.True(reg.Success);

        var ver = await api.VerifyEmailAsync("user@example.com", "123456");
        Assert.True(ver.Success);
        Assert.NotNull(ver.User);
        Assert.Equal("user@example.com", ver.User.Email);
        Assert.NotNull(ver.Tokens);
        Assert.Equal("jwt-access-123", ver.Tokens.AccessToken);
    }

    [Fact]
    public async Task AuthApiClient_LoginAndRefresh_RotatesTokens()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var login = await api.LoginAsync("user@example.com", "Password123!");
        Assert.True(login.Success);
        Assert.NotNull(login.Tokens);

        var refresh = await api.RefreshAsync(login.Tokens.RefreshToken);
        Assert.True(refresh.Success);
        Assert.NotNull(refresh.Tokens);
        Assert.Equal("jwt-refreshed-111", refresh.Tokens.AccessToken);
    }

    [Fact]
    public async Task AuthApiClient_GetProfileAndPreferences_PassesBearerHeader()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var profile = await api.GetProfileAsync("secret-bearer-jwt");
        Assert.NotNull(profile);
        Assert.Equal("Bearer secret-bearer-jwt", handler.LastAuthHeader);
        Assert.True(profile.PresenceConsent);

        var prefOk = await api.UpdatePreferencesAsync("secret-bearer-jwt", presenceConsent: true, telemetryConsent: true);
        Assert.True(prefOk);
        Assert.Equal("Bearer secret-bearer-jwt", handler.LastAuthHeader);
    }

    [Fact]
    public async Task AuthApiClient_OfflineFallback_HandlesGracefullyWithoutThrowing()
    {
        var handler = new MockAuthHttpHandler { SimulateFailure = true };
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var res = await api.LoginAsync("user@example.com", "Password123!");
        Assert.False(res.Success);
        Assert.Contains("Could not connect", res.Message);
    }
}
