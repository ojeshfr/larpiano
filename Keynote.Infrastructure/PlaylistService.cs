using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Keynote.Core;

namespace Keynote.Infrastructure;

public interface IPlaylistService
{
    Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken token = default);
    Task<Playlist?> GetPlaylistAsync(string playlistId, CancellationToken token = default);
    Task<Playlist> CreatePlaylistAsync(string name, CancellationToken token = default);
    Task<Playlist?> UpdatePlaylistAsync(string playlistId, string name, CancellationToken token = default);
    Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken token = default);
    Task<PlaylistItem?> AddItemAsync(string playlistId, string songSha256, string title, string artist, double durationSeconds, CancellationToken token = default);
    Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken token = default);
    Task<bool> ReorderItemsAsync(string playlistId, IReadOnlyList<string> itemIds, CancellationToken token = default);
    Task<Playlist?> DuplicatePlaylistAsync(string playlistId, string? newName = null, CancellationToken token = default);
}

public sealed class LocalPlaylistStore : IPlaylistService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LocalPlaylistStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KEYNOTE");
            _filePath = Path.Combine(appData, "guest_playlists.json");
        }
    }

    private async Task<List<Playlist>> ReadAllAsync(CancellationToken token)
    {
        if (!File.Exists(_filePath))
            return new List<Playlist>();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var items = await JsonSerializer.DeserializeAsync<List<Playlist>>(stream, JsonOpts, token).ConfigureAwait(false);
            return items ?? new List<Playlist>();
        }
        catch
        {
            return new List<Playlist>();
        }
    }

    private async Task WriteAllAsync(List<Playlist> playlists, CancellationToken token)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, playlists, JsonOpts, token).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken token = default)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            return list.OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt ?? DateTimeOffset.MinValue).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Playlist?> GetPlaylistAsync(string playlistId, CancellationToken token = default)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            return list.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Playlist> CreatePlaylistAsync(string name, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var p = new Playlist(
                Id: Guid.NewGuid().ToString(),
                Name: name.Trim(),
                UserId: "guest",
                IsPublic: false,
                ItemCount: 0,
                TotalDuration: 0.0,
                Items: Array.Empty<PlaylistItem>(),
                CreatedAt: now,
                UpdatedAt: now
            );
            list.Add(p);
            await WriteAllAsync(list, token).ConfigureAwait(false);
            return p;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Playlist?> UpdatePlaylistAsync(string playlistId, string name, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            var idx = list.FindIndex(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null;

            var existing = list[idx];
            var updated = existing with
            {
                Name = name.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            list[idx] = updated;
            await WriteAllAsync(list, token).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken token = default)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            var removed = list.RemoveAll(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await WriteAllAsync(list, token).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PlaylistItem?> AddItemAsync(string playlistId, string songSha256, string title, string artist, double durationSeconds, CancellationToken token = default)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            var idx = list.FindIndex(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null;

            var existing = list[idx];
            var items = existing.Items?.ToList() ?? new List<PlaylistItem>();
            var now = DateTimeOffset.UtcNow;
            var item = new PlaylistItem(
                Id: Guid.NewGuid().ToString(),
                PlaylistId: playlistId,
                SongSha256: songSha256,
                Title: title,
                Artist: artist,
                DurationSeconds: durationSeconds,
                OrderIndex: items.Count,
                CreatedAt: now
            );
            items.Add(item);

            var updated = existing with
            {
                Items = items,
                ItemCount = items.Count,
                TotalDuration = items.Sum(i => i.DurationSeconds),
                UpdatedAt = now
            };
            list[idx] = updated;
            await WriteAllAsync(list, token).ConfigureAwait(false);
            return item;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken token = default)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            var idx = list.FindIndex(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null == null && false;

            var existing = list[idx];
            var items = existing.Items?.ToList() ?? new List<PlaylistItem>();
            var removed = items.RemoveAll(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;

            // Re-index
            for (int i = 0; i < items.Count; i++)
            {
                items[i] = items[i] with { OrderIndex = i };
            }

            var updated = existing with
            {
                Items = items,
                ItemCount = items.Count,
                TotalDuration = items.Sum(i => i.DurationSeconds),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            list[idx] = updated;
            await WriteAllAsync(list, token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ReorderItemsAsync(string playlistId, IReadOnlyList<string> itemIds, CancellationToken token = default)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllAsync(token).ConfigureAwait(false);
            var idx = list.FindIndex(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;

            var existing = list[idx];
            var currentItems = existing.Items?.ToList() ?? new List<PlaylistItem>();
            var itemMap = currentItems.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

            var reordered = new List<PlaylistItem>();
            foreach (var id in itemIds)
            {
                if (itemMap.TryGetValue(id, out var it))
                    reordered.Add(it with { OrderIndex = reordered.Count });
            }
            foreach (var remaining in currentItems.Where(i => !itemIds.Contains(i.Id, StringComparer.OrdinalIgnoreCase)))
            {
                reordered.Add(remaining with { OrderIndex = reordered.Count });
            }

            var updated = existing with
            {
                Items = reordered,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            list[idx] = updated;
            await WriteAllAsync(list, token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Playlist?> DuplicatePlaylistAsync(string playlistId, string? newName = null, CancellationToken token = default)
    {
        var orig = await GetPlaylistAsync(playlistId, token).ConfigureAwait(false);
        if (orig == null) return null;

        var name = (newName ?? $"{orig.Name} (Copy)").Trim();
        var created = await CreatePlaylistAsync(name, token).ConfigureAwait(false);
        if (orig.Items != null)
        {
            foreach (var it in orig.Items)
            {
                await AddItemAsync(created.Id, it.SongSha256, it.Title, it.Artist, it.DurationSeconds, token).ConfigureAwait(false);
            }
        }
        return await GetPlaylistAsync(created.Id, token).ConfigureAwait(false);
    }
}

public sealed class RemotePlaylistService : IPlaylistService
{
    private readonly HttpClient _http;
    private readonly IAuthTokenStore _tokenStore;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static async Task<T?> ReadJsonAsync<T>(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, token).ConfigureAwait(false);
    }


    public RemotePlaylistService(HttpClient http, IAuthTokenStore tokenStore, string? baseUrl = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("LARPIANO_API_URL") ?? "https://larpiano.portways.app").TrimEnd('/');
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string path, CancellationToken token)
    {
        var tokens = await _tokenStore.LoadTokensAsync(token).ConfigureAwait(false);
        var req = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (tokens != null && !string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }
        return req;
    }

    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, "/api/playlists", token).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var res = await ReadJsonAsync<List<Playlist>>(resp.Content, token).ConfigureAwait(false);
        return res ?? (IReadOnlyList<Playlist>)Array.Empty<Playlist>();
    }

    public async Task<Playlist?> GetPlaylistAsync(string playlistId, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"/api/playlists/{Uri.EscapeDataString(playlistId)}", token).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await ReadJsonAsync<Playlist>(resp.Content, token).ConfigureAwait(false);
    }

    public async Task<Playlist> CreatePlaylistAsync(string name, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/playlists", token).ConfigureAwait(false);
        req.Content = new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync<Playlist>(resp.Content, token).ConfigureAwait(false))!;
    }

    public async Task<Playlist?> UpdatePlaylistAsync(string playlistId, string name, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Put, $"/api/playlists/{Uri.EscapeDataString(playlistId)}", token).ConfigureAwait(false);
        req.Content = new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await ReadJsonAsync<Playlist>(resp.Content, token).ConfigureAwait(false);
    }

    public async Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Delete, $"/api/playlists/{Uri.EscapeDataString(playlistId)}", token).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<PlaylistItem?> AddItemAsync(string playlistId, string songSha256, string title, string artist, double durationSeconds, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, $"/api/playlists/{Uri.EscapeDataString(playlistId)}/items", token).ConfigureAwait(false);
        req.Content = new StringContent(JsonSerializer.Serialize(new { sha256 = songSha256, title, artist, duration_seconds = durationSeconds }), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await ReadJsonAsync<PlaylistItem>(resp.Content, token).ConfigureAwait(false);
    }

    public async Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Delete, $"/api/playlists/{Uri.EscapeDataString(playlistId)}/items/{Uri.EscapeDataString(itemId)}", token).ConfigureAwait(false);
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> ReorderItemsAsync(string playlistId, IReadOnlyList<string> itemIds, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Put, $"/api/playlists/{Uri.EscapeDataString(playlistId)}/reorder", token).ConfigureAwait(false);
        req.Content = new StringContent(JsonSerializer.Serialize(new { item_ids = itemIds }), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<Playlist?> DuplicatePlaylistAsync(string playlistId, string? newName = null, CancellationToken token = default)
    {
        using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, $"/api/playlists/{Uri.EscapeDataString(playlistId)}/duplicate", token).ConfigureAwait(false);
        req.Content = new StringContent(JsonSerializer.Serialize(new { name = newName }), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await ReadJsonAsync<Playlist>(resp.Content, token).ConfigureAwait(false);
    }
}

public sealed class CompositePlaylistService : IPlaylistService
{
    private readonly LocalPlaylistStore _local;
    private readonly RemotePlaylistService _remote;
    private readonly IAuthTokenStore _tokenStore;

    public CompositePlaylistService(LocalPlaylistStore local, RemotePlaylistService remote, IAuthTokenStore tokenStore)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
    }

    private async Task<bool> IsAuthenticatedAsync(CancellationToken token)
    {
        var stored = await _tokenStore.LoadTokensAsync(token).ConfigureAwait(false);
        return stored != null && !string.IsNullOrWhiteSpace(stored.AccessToken);
    }

    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                return await _remote.GetPlaylistsAsync(token).ConfigureAwait(false);
            }
            catch
            {
                // Fall back to local if offline or network unreachable
            }
        }
        return await _local.GetPlaylistsAsync(token).ConfigureAwait(false);
    }

    public async Task<Playlist?> GetPlaylistAsync(string playlistId, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var r = await _remote.GetPlaylistAsync(playlistId, token).ConfigureAwait(false);
                if (r != null) return r;
            }
            catch { }
        }
        return await _local.GetPlaylistAsync(playlistId, token).ConfigureAwait(false);
    }

    public async Task<Playlist> CreatePlaylistAsync(string name, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                return await _remote.CreatePlaylistAsync(name, token).ConfigureAwait(false);
            }
            catch { }
        }
        return await _local.CreatePlaylistAsync(name, token).ConfigureAwait(false);
    }

    public async Task<Playlist?> UpdatePlaylistAsync(string playlistId, string name, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var r = await _remote.UpdatePlaylistAsync(playlistId, name, token).ConfigureAwait(false);
                if (r != null) return r;
            }
            catch { }
        }
        return await _local.UpdatePlaylistAsync(playlistId, name, token).ConfigureAwait(false);
    }

    public async Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var ok = await _remote.DeletePlaylistAsync(playlistId, token).ConfigureAwait(false);
                if (ok) return true;
            }
            catch { }
        }
        return await _local.DeletePlaylistAsync(playlistId, token).ConfigureAwait(false);
    }

    public async Task<PlaylistItem?> AddItemAsync(string playlistId, string songSha256, string title, string artist, double durationSeconds, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var item = await _remote.AddItemAsync(playlistId, songSha256, title, artist, durationSeconds, token).ConfigureAwait(false);
                if (item != null) return item;
            }
            catch { }
        }
        return await _local.AddItemAsync(playlistId, songSha256, title, artist, durationSeconds, token).ConfigureAwait(false);
    }

    public async Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var ok = await _remote.RemoveItemAsync(playlistId, itemId, token).ConfigureAwait(false);
                if (ok) return true;
            }
            catch { }
        }
        return await _local.RemoveItemAsync(playlistId, itemId, token).ConfigureAwait(false);
    }

    public async Task<bool> ReorderItemsAsync(string playlistId, IReadOnlyList<string> itemIds, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var ok = await _remote.ReorderItemsAsync(playlistId, itemIds, token).ConfigureAwait(false);
                if (ok) return true;
            }
            catch { }
        }
        return await _local.ReorderItemsAsync(playlistId, itemIds, token).ConfigureAwait(false);
    }

    public async Task<Playlist?> DuplicatePlaylistAsync(string playlistId, string? newName = null, CancellationToken token = default)
    {
        if (await IsAuthenticatedAsync(token).ConfigureAwait(false))
        {
            try
            {
                var p = await _remote.DuplicatePlaylistAsync(playlistId, newName, token).ConfigureAwait(false);
                if (p != null) return p;
            }
            catch { }
        }
        return await _local.DuplicatePlaylistAsync(playlistId, newName, token).ConfigureAwait(false);
    }
}
