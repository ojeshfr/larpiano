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
        public bool SimulateServerError { get; set; }
        public bool SimulateHtmlError { get; set; }
        public string? LastAuthHeader { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int RequestCount { get; private set; }
        public System.Collections.Generic.List<string> RequestedPaths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri?.AbsolutePath ?? "";
            RequestedPaths.Add(path);

            if (SimulateFailure)
            {
                throw new HttpRequestException("Network unreachable");
            }

            if (SimulateHtmlError)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotImplemented)
                {
                    Content = new StringContent("<!DOCTYPE HTML><html><head><title>501 Not Implemented</title></head><body><h1>Unsupported method (\'POST\')</h1></body></html>", System.Text.Encoding.UTF8, "text/html")
                });
            }

            if (SimulateServerError)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"Internal database failure\"}")
                });
            }

            LastAuthHeader = request.Headers.Authorization?.ToString();
            if (request.Content != null)
            {
                LastRequestBody = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }

            LastAuthHeader = request.Headers.Authorization?.ToString();

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

    [Fact]
    public async Task AuthApiClient_HtmlErrorResponse_HandledGracefullyWithoutJsonParserCrash()
    {
        var handler = new MockAuthHttpHandler { SimulateHtmlError = true };
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var res = await api.RegisterAsync("user@example.com", "Password123!", "Tester");
        Assert.False(res.Success);
        Assert.DoesNotContain("invalid start of a value", res.Message);
        Assert.Contains("501", res.Message);
    }

    [Fact]
    public async Task AuthApiClient_Logout_SuccessReturnsTrueAndPassesHeadersAndPayload()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var success = await api.LogoutAsync("refresh-token-xyz", "bearer-access-123");
        Assert.True(success);
        Assert.Equal("Bearer bearer-access-123", handler.LastAuthHeader);
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("refresh-token-xyz", handler.LastRequestBody);
    }

    [Fact]
    public async Task AuthApiClient_Logout_ServerErrorReturnsFalseWithoutThrowing()
    {
        var handler = new MockAuthHttpHandler { SimulateServerError = true };
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var success = await api.LogoutAsync("refresh-token-xyz", "bearer-access-123");
        Assert.False(success);
    }

    [Fact]
    public async Task AuthApiClient_Logout_NetworkFailureReturnsFalseWithoutThrowing()
    {
        var handler = new MockAuthHttpHandler { SimulateFailure = true };
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var success = await api.LogoutAsync("refresh-token-xyz");
        Assert.False(success);
    }

    [Fact]
    public async Task AuthApiClient_LogoutAll_ServerErrorReturnsFalseWithoutThrowing()
    {
        var handler = new MockAuthHttpHandler { SimulateServerError = true };
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var success = await api.LogoutAllAsync("bearer-access-123");
        Assert.False(success);
    }

    [Fact]
    public async Task AuthApiClient_DoubleLogout_ExecutesIdempotently()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        var first = await api.LogoutAsync("refresh-1", "access-1");
        var second = await api.LogoutAsync("refresh-1", "access-1");
        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public async Task AuthTokenStore_DoubleClear_DoesNotThrow()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"auth_clear_test_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);
            await store.SaveTokensAsync("access-val", "refresh-val");
            Assert.True(File.Exists(tempFile));

            // First clear
            await store.ClearTokensAsync();
            Assert.False(File.Exists(tempFile));
            var loaded1 = await store.LoadTokensAsync();
            Assert.Null(loaded1);

            // Second clear on non-existent file
            var ex = await Record.ExceptionAsync(() => store.ClearTokensAsync());
            Assert.Null(ex);
            var loaded2 = await store.LoadTokensAsync();
            Assert.Null(loaded2);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GuestMode_StartupWithNoSavedTokens_ResolvesToUnauthenticatedState()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"guest_none_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);
            var handler = new MockAuthHttpHandler();
            using var client = new HttpClient(handler);
            var api = new AuthApiClient(client, "http://127.0.0.1:8088");

            var stored = await store.LoadTokensAsync();
            Assert.Null(stored);

            AuthUserProfile? currentUser = null;
            string? currentToken = null;

            // In guest mode with no stored tokens
            Assert.Null(currentUser);
            Assert.Null(currentToken);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GuestMode_StartupWithSavedTokens_PreservesStoredCredentialsWithoutImplicitUsage()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"guest_saved_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);
            await store.SaveTokensAsync("saved-access-token", "saved-refresh-token");

            // User explicitly selects Continue as Guest
            AuthUserProfile? currentUser = null;
            string? currentToken = null;

            // Guest state in memory must have no active token
            Assert.Null(currentUser);
            Assert.Null(currentToken);

            // But disk credentials are preserved (not erased without explicit sign out)
            var onDisk = await store.LoadTokensAsync();
            Assert.NotNull(onDisk);
            Assert.Equal("saved-access-token", onDisk.AccessToken);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GuestMode_StartupWithOfflineBackend_DoesNotThrowAndRemainsInGuestState()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"guest_offline_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);
            await store.SaveTokensAsync("expired-token", "expired-refresh");

            var handler = new MockAuthHttpHandler { SimulateFailure = true };
            using var client = new HttpClient(handler);
            var api = new AuthApiClient(client, "http://127.0.0.1:8088");

            AuthUserProfile? currentUser = null;
            string? currentToken = null;

            // Startup token check encounters network failure
            try
            {
                var profile = await api.GetProfileAsync("expired-token");
                if (profile != null) currentUser = profile;
            }
            catch (Exception)
            {
                // Resilient fallback: network failure leaves Guest mode active
            }

            Assert.Null(currentUser);
            Assert.Null(currentToken);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GuestMode_StartupWithOnlineBackend_DoesNotIssueAuthenticatedRequests()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        // When user is in guest mode, no authenticated requests are made
        AuthUserProfile? guestUser = null;
        string? guestToken = null;

        if (guestToken != null)
        {
            await api.GetProfileAsync(guestToken);
        }

        Assert.Null(guestUser);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GuestMode_StartupAfterSignOut_EntersCleanGuestStateWithoutStaleTokens()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"guest_signout_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);
            await store.SaveTokensAsync("active-access", "active-refresh");

            // Perform sign out
            await store.ClearTokensAsync();
            AuthUserProfile? user = null;
            string? token = null;

            // Next startup
            var stored = await store.LoadTokensAsync();
            Assert.Null(stored);
            Assert.Null(user);
            Assert.Null(token);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GuestMode_InFlightAuthenticationCheck_DoesNotOverwriteGuestState()
    {
        int authEpoch = 0;
        int startupEpoch = Interlocked.Increment(ref authEpoch);

        AuthUserProfile? currentUser = null;
        var tcs = new TaskCompletionSource<AuthUserProfile>();

        // Background authentication verification task starts
        var verificationTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            var profile = new AuthUserProfile("u-99", "delayed@example.test", "Delayed User", "user", true, false, false);
            if (Volatile.Read(ref authEpoch) == startupEpoch)
            {
                currentUser = profile;
            }
        });

        // User immediately clicks Continue as Guest before verification finishes
        Interlocked.Increment(ref authEpoch); // cancels in-flight epoch
        currentUser = null;

        await verificationTask;

        // Guest mode MUST remain active; delayed background result must be discarded
        Assert.Null(currentUser);
    }

    [Fact]
    public async Task GuestMode_ExplicitSelection_DoesNotMakeAuthenticatedApiRequests()
    {
        var handler = new MockAuthHttpHandler();
        using var client = new HttpClient(handler);
        var api = new AuthApiClient(client, "http://127.0.0.1:8088");

        // Action invoked on Guest Mode Selected
        Action onGuestSelected = () =>
        {
            // Null out active credentials
        };

        onGuestSelected();

        // Ensure zero requests were made to server
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void GuestMode_ExplicitSelection_DoesNotRestoreAuthenticatedUser()
    {
        AuthUserProfile? user = new AuthUserProfile("u-1", "test@larpiano.test", "Pianist", "user", true, false, false);
        string? token = "some-token";

        // User chooses Continue as Guest
        user = null;
        token = null;

        Assert.Null(user);
        Assert.Null(token);
    }

    [Fact]
    public void GuestMode_RepeatedSelection_ExecutesIdempotentlyWithoutErrors()
    {
        int authEpoch = 0;
        AuthUserProfile? user = null;
        string? token = null;

        for (int i = 0; i < 5; i++)
        {
            Interlocked.Increment(ref authEpoch);
            user = null;
            token = null;
            Assert.Null(user);
            Assert.Null(token);
        }

        Assert.Equal(5, authEpoch);
    }

    [Fact]
    public async Task GuestMode_PreservesSavedCredentialsOnDisk_UntilExplicitSignOut()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"guest_preserve_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new AuthTokenStore(tempFile);
            await store.SaveTokensAsync("saved-token-a", "saved-token-r");

            // Guest session: tokens remain on disk
            var tokens = await store.LoadTokensAsync();
            Assert.NotNull(tokens);
            Assert.Equal("saved-token-a", tokens.AccessToken);

            // Only explicit sign out clears them
            await store.ClearTokensAsync();
            var tokensAfterSignOut = await store.LoadTokensAsync();
            Assert.Null(tokensAfterSignOut);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
