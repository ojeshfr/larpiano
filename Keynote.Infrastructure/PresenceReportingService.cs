using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Keynote.Infrastructure;

public sealed record AppAnnouncement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("published_at")] string? PublishedAt,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt
);

public sealed record AppPlaygroundEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("effect_type")] string EffectType,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt
);

public interface IPresenceReportingService : IAsyncDisposable
{
    string SessionIdentifier { get; }
    string AppVersion { get; }
    bool IsConsentGranted { get; set; }
    string CurrentStatus { get; set; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppAnnouncement>> GetAnnouncementsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppPlaygroundEvent>> GetPlaygroundEventsAsync(CancellationToken cancellationToken = default);
    Task DismissPlaygroundEventAsync(string eventId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Client presence and coarse status reporting service.
/// 
/// Privacy & Safety Guarantees:
/// - Strictly opt-in telemetry and presence reporting.
/// - Guest mode is default: local MIDI playback never blocked or stalled.
/// - Zero collection of: Roblox credentials, private files, keystrokes, or process lists.
/// - Reports coarse status only ('online', 'idle', 'playing') every 45s.
/// - Cleanly closes the active session on application shutdown.
/// </summary>
public sealed class PresenceReportingService : IPresenceReportingService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _appVersion;
    private readonly string _sessionIdentifier;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatLoop;
    private string _currentStatus = "online";
    private bool _isConsentGranted = false;

    public string SessionIdentifier => _sessionIdentifier;
    public string AppVersion => _appVersion;
    public string? AccessToken { get; set; }

    public bool IsConsentGranted
    {
        get => _isConsentGranted;
        set => _isConsentGranted = value;
    }

    public string CurrentStatus
    {
        get => _currentStatus;
        set
        {
            if (value is "online" or "idle" or "playing" or "offline")
            {
                _currentStatus = value;
            }
        }
    }

    public PresenceReportingService(HttpClient? httpClient = null, string? baseUrl = null, string? appVersion = "v1.1.0")
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("LARPIANO_API_URL") ?? "https://larpiano.portways.app").TrimEnd('/');
        _appVersion = appVersion ?? "v1.1.0";
        _sessionIdentifier = "wpf-" + Guid.NewGuid().ToString("N")[..16];
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_heartbeatLoop != null && !_heartbeatLoop.IsCompleted)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        // Initial heartbeat
        await SendHeartbeatAsync(token).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(45));
        try
        {
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (_isConsentGranted)
                {
                    await SendHeartbeatAsync(token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on application exit
        }
        catch (Exception)
        {
            // Resilient silent fallback: local playback must never be interrupted
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken token)
    {
        if (!_isConsentGranted || string.IsNullOrWhiteSpace(AccessToken)) return;

        try
        {
            var payload = new
            {
                session_identifier = _sessionIdentifier,
                app_version = _appVersion,
                status = _currentStatus
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/app/sessions/heartbeat") { Content = JsonContent.Create(payload) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Ignore transient network errors
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        if (_heartbeatLoop != null)
        {
            try
            {
                await _heartbeatLoop.ConfigureAwait(false);
            }
            catch (Exception) { }
            _heartbeatLoop = null;
        }

        if (_isConsentGranted && !string.IsNullOrWhiteSpace(AccessToken))
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var payload = new { session_identifier = _sessionIdentifier };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/app/sessions/end") { Content = JsonContent.Create(payload) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
                using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort session cleanup
            }
        }
    }

    public async Task<IReadOnlyList<AppAnnouncement>> GetAnnouncementsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _http.GetFromJsonAsync<AppAnnouncement[]>($"{_baseUrl}/api/app/announcements", cancellationToken).ConfigureAwait(false);
            return results ?? Array.Empty<AppAnnouncement>();
        }
        catch (Exception)
        {
            return Array.Empty<AppAnnouncement>();
        }
    }

    public async Task<IReadOnlyList<AppPlaygroundEvent>> GetPlaygroundEventsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _http.GetFromJsonAsync<AppPlaygroundEvent[]>($"{_baseUrl}/api/app/playground/events", cancellationToken).ConfigureAwait(false);
            return results ?? Array.Empty<AppPlaygroundEvent>();
        }
        catch (Exception)
        {
            return Array.Empty<AppPlaygroundEvent>();
        }
    }

    public async Task DismissPlaygroundEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _http.PostAsync($"{_baseUrl}/api/app/playground/events/{Uri.EscapeDataString(eventId)}/dismiss", null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
