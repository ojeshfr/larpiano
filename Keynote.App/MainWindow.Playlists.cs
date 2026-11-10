using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Keynote.Core;
using Keynote.Infrastructure;
using Keynote.Online;

namespace Keynote.App;

public partial class MainWindow
{
    readonly PlaylistManager playlistManager = new();
    IPlaylistService? playlistService;
    IReadOnlyList<Playlist> cachedPlaylists = Array.Empty<Playlist>();
    bool playlistsInitialized;

    void InitializePlaylists()
    {
        if (playlistsInitialized) return;
        playlistsInitialized = true;

        var localStore = new LocalPlaylistStore();
        var remoteStore = new RemotePlaylistService(onlineHttp ?? new System.Net.Http.HttpClient(), authTokenStore);
        playlistService = new CompositePlaylistService(localStore, remoteStore, authTokenStore);

        playlistManager.PlayTrackHandler = PlayPlaylistItemAsync;
        playlistManager.NotifyStatusHandler = msg =>
        {
            if (Status != null) Status.Text = msg;
        };

        engine.PlaybackCompleted += OnEnginePlaybackCompleted;
        _ = ReloadPlaylistsAsync();
    }

    void OnEnginePlaybackCompleted(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (playlistManager.IsPlayingPlaylist)
            {
                await playlistManager.OnSongPlaybackCompletedAsync();
            }
        });
    }

    public async Task ReloadPlaylistsAsync()
    {
        if (playlistService == null) return;
        try
        {
            var lists = await playlistService.GetPlaylistsAsync();
            cachedPlaylists = lists;
            if (playlistManager.CurrentPlaylist != null)
            {
                var refreshed = lists.FirstOrDefault(p => p.Id == playlistManager.CurrentPlaylist.Id);
                if (refreshed != null && refreshed.Items != null)
                {
                    // Refresh current playlist if items changed
                    playlistManager.LoadPlaylist(refreshed);
                }
            }
        }
        catch
        {
            // Offline or store error
        }
    }

    public async Task<bool> PlayPlaylistItemAsync(PlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var entry = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, item.SongSha256, StringComparison.OrdinalIgnoreCase))
                    ?? libraryEntries.FirstOrDefault(e => string.Equals(e.Title, item.Title, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Artist, item.Artist, StringComparison.OrdinalIgnoreCase));

        // If not locally cached, attempt download from cloud catalog
        if (entry == null)
        {
            if (Status != null) Status.Text = $"Downloading {item.Title}…";
            var cloudSong = modernCloudSongs.FirstOrDefault(s => string.Equals(s.Sha256, item.SongSha256, StringComparison.OrdinalIgnoreCase));
            if (cloudSong != null && modernCloudCatalog != null && library != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var saved = await modernCloudCatalog.DownloadAsync(cloudSong, library, null, cts.Token);
                    await ReloadLibrary(CancellationToken.None);
                    entry = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, saved.Entry.Sha256, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    if (Status != null) Status.Text = $"Could not download {item.Title}: {ex.Message}";
                    return false;
                }
            }
            else
            {
                if (Status != null) Status.Text = $"Song not downloaded: {item.Title} (Offline or missing).";
                return false;
            }
        }

        if (entry == null) return false;

        // Load into player
        PlayerSongSelector.SelectedItem = entry;
        await LoadLibrarySelection(entry, () => PlayerSongSelector.SelectedItem as LibraryEntry);

        // If game window is foreground, start immediately
        if (input.IsTargetForeground && profile.Verified && song != null)
        {
            try
            {
                StartImmediate();
                return true;
            }
            catch (Exception ex)
            {
                if (Status != null) Status.Text = ex.Message;
                return false;
            }
        }
        else
        {
            if (Status != null) Status.Text = $"Ready: {item.Title} — Focus game window to begin.";
            hotkeyController.SetReady(true);
            return true;
        }
    }

    public void SelectPlaylistFromOverlay(Playlist? p)
    {
        if (p == null)
        {
            playlistManager.LoadPlaylist(null);
            return;
        }

        _ = Task.Run(async () =>
        {
            var full = await playlistService!.GetPlaylistAsync(p.Id);
            Dispatcher.Invoke(() =>
            {
                playlistManager.LoadPlaylist(full ?? p);
                if (playlistManager.Queue.Count > 0)
                {
                    _ = playlistManager.StartPlaylistAsync();
                }
            });
        });
    }

    public async void PlayNextFromOverlay()
    {
        await playlistManager.PlayNextAsync();
    }

    public async void PlayPreviousFromOverlay()
    {
        await playlistManager.PlayPreviousAsync();
    }

    public void ToggleShuffleFromOverlay()
    {
        playlistManager.ToggleShuffle();
    }

    public void CycleRepeatFromOverlay()
    {
        playlistManager.CycleRepeatMode();
    }

    public async void PlayQueueItemFromOverlay(PlaylistItem item)
    {
        int idx = playlistManager.Queue.ToList().FindIndex(i => i.Id == item.Id);
        if (idx >= 0)
        {
            await playlistManager.PlayTrackAtAsync(idx);
        }
    }

    public async void RemoveFromQueueFromOverlay(string id)
    {
        playlistManager.RemoveFromQueue(id);
        if (playlistManager.CurrentPlaylist != null && playlistService != null)
        {
            await playlistService.RemoveItemAsync(playlistManager.CurrentPlaylist.Id, id);
            await ReloadPlaylistsAsync();
        }
    }

    public void ClearQueueFromOverlay()
    {
        playlistManager.ClearQueue();
    }

    public async Task AddSongToPlaylistAsync(string playlistId, string sha256, string title, string artist, double durationSeconds)
    {
        if (playlistService == null) return;
        await playlistService.AddItemAsync(playlistId, sha256, title, artist, durationSeconds);
        await ReloadPlaylistsAsync();
    }
}
