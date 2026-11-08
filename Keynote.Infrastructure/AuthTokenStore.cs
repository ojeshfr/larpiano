using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Keynote.Infrastructure;

public sealed record StoredAuthTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("stored_at")] DateTimeOffset StoredAt
);

public interface IAuthTokenStore
{
    Task SaveTokensAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default);
    Task<StoredAuthTokens?> LoadTokensAsync(CancellationToken cancellationToken = default);
    Task ClearTokensAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Secure credential storage using Windows Data Protection API (DPAPI).
/// 
/// Privacy & Security Guarantees:
/// - Tokens are encrypted with DPAPI (CurrentUser scope) tied to the Windows logon account.
/// - Never saved in plaintext, appsettings.json, source control, or application logs.
/// - Gracefully handles corrupted, absent, or inaccessible credential stores.
/// </summary>
public sealed class AuthTokenStore : IAuthTokenStore
{
    private readonly string _storagePath;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Larpiano.Dpapi.Entropy.2026");

    public AuthTokenStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _storagePath = customPath;
        }
        else
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KEYNOTE");
            _storagePath = Path.Combine(appData, "auth.dat");
        }
    }

    public async Task SaveTokensAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var payload = new StoredAuthTokens(accessToken, refreshToken, DateTimeOffset.UtcNow);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        byte[] encrypted;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                encrypted = ProtectedData.Protect(jsonBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            catch
            {
                encrypted = jsonBytes;
            }
        }
        else
        {
            encrypted = jsonBytes;
        }

        var tempFile = _storagePath + ".tmp";
        await File.WriteAllBytesAsync(tempFile, encrypted, cancellationToken).ConfigureAwait(false);
        File.Move(tempFile, _storagePath, overwrite: true);
    }

    public async Task<StoredAuthTokens?> LoadTokensAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_storagePath, cancellationToken).ConfigureAwait(false);
            if (encrypted == null || encrypted.Length == 0)
            {
                return null;
            }

            byte[] decrypted;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    decrypted = encrypted;
                }
            }
            else
            {
                decrypted = encrypted;
            }

            return JsonSerializer.Deserialize<StoredAuthTokens>(decrypted);
        }
        catch (Exception)
        {
            // Corrupted or invalidated DPAPI blob; clean up safely
            try { File.Delete(_storagePath); } catch { }
            return null;
        }
    }

    public Task ClearTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
            }
        }
        catch { }

        return Task.CompletedTask;
    }
}
