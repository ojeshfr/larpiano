using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Keynote.Core;

namespace Keynote.Infrastructure;

public sealed class PlaylistManager
{
    private readonly object _gate = new();
    private Playlist? _currentPlaylist;
    private readonly List<PlaylistItem> _queue = new();
    private readonly List<int> _playbackOrder = new();
    private int _orderIndex = -1;
    private bool _isShuffleEnabled;
    private PlaylistRepeatMode _repeatMode = PlaylistRepeatMode.None;
    private bool _isPlayingPlaylist;

    public Func<PlaylistItem, Task<bool>>? PlayTrackHandler { get; set; }
    public Action<string>? NotifyStatusHandler { get; set; }

    public event EventHandler? StateChanged;

    public Playlist? CurrentPlaylist
    {
        get { lock (_gate) return _currentPlaylist; }
    }

    public IReadOnlyList<PlaylistItem> Queue
    {
        get { lock (_gate) return _queue.ToArray(); }
    }

    public PlaylistItem? CurrentTrack
    {
        get
        {
            lock (_gate)
            {
                if (_orderIndex >= 0 && _orderIndex < _playbackOrder.Count)
                {
                    int qIdx = _playbackOrder[_orderIndex];
                    if (qIdx >= 0 && qIdx < _queue.Count)
                        return _queue[qIdx];
                }
                return null;
            }
        }
    }

    public int CurrentTrackNumber
    {
        get
        {
            lock (_gate) return _orderIndex >= 0 ? _orderIndex + 1 : 0;
        }
    }

    public int TotalTracks
    {
        get
        {
            lock (_gate) return _queue.Count;
        }
    }

    public bool IsShuffleEnabled
    {
        get { lock (_gate) return _isShuffleEnabled; }
    }

    public PlaylistRepeatMode RepeatMode
    {
        get { lock (_gate) return _repeatMode; }
    }

    public bool IsPlayingPlaylist
    {
        get { lock (_gate) return _isPlayingPlaylist; }
    }

    public void LoadPlaylist(Playlist? playlist, int startIndex = 0)
    {
        lock (_gate)
        {
            _currentPlaylist = playlist;
            _queue.Clear();
            if (playlist?.Items != null)
            {
                _queue.AddRange(playlist.Items);
            }
            RebuildOrder(startIndex);
            _isPlayingPlaylist = false;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildOrder(int startIndex)
    {
        _playbackOrder.Clear();
        int count = _queue.Count;
        if (count == 0)
        {
            _orderIndex = -1;
            return;
        }

        var indices = Enumerable.Range(0, count).ToList();
        if (_isShuffleEnabled)
        {
            // Fisher-Yates shuffle
            var rng = Random.Shared;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            // If startIndex is specified, move it to the front
            int targetPos = indices.IndexOf(Math.Clamp(startIndex, 0, count - 1));
            if (targetPos > 0)
            {
                (indices[0], indices[targetPos]) = (indices[targetPos], indices[0]);
            }
        }
        else
        {
            // Linear order
            int clampedStart = Math.Clamp(startIndex, 0, count - 1);
            indices = Enumerable.Range(0, count).ToList();
            _orderIndex = clampedStart;
            _playbackOrder.AddRange(indices);
            return;
        }

        _playbackOrder.AddRange(indices);
        _orderIndex = 0;
    }

    public void ToggleShuffle()
    {
        lock (_gate)
        {
            _isShuffleEnabled = !_isShuffleEnabled;
            int currentQueueIndex = -1;
            if (_orderIndex >= 0 && _orderIndex < _playbackOrder.Count)
            {
                currentQueueIndex = _playbackOrder[_orderIndex];
            }
            RebuildOrder(currentQueueIndex >= 0 ? currentQueueIndex : 0);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CycleRepeatMode()
    {
        lock (_gate)
        {
            _repeatMode = _repeatMode switch
            {
                PlaylistRepeatMode.None => PlaylistRepeatMode.All,
                PlaylistRepeatMode.All => PlaylistRepeatMode.One,
                _ => PlaylistRepeatMode.None
            };
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> StartPlaylistAsync()
    {
        PlaylistItem? track;
        lock (_gate)
        {
            if (_queue.Count == 0) return false;
            if (_orderIndex < 0 || _orderIndex >= _playbackOrder.Count)
            {
                RebuildOrder(0);
            }
            _isPlayingPlaylist = true;
            track = CurrentTrack;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (track != null && PlayTrackHandler != null)
        {
            return await PlayTrackHandler(track).ConfigureAwait(false);
        }
        return false;
    }

    public async Task<bool> PlayTrackAtAsync(int queueIndex)
    {
        PlaylistItem? track;
        lock (_gate)
        {
            if (queueIndex < 0 || queueIndex >= _queue.Count) return false;
            int orderPos = _playbackOrder.IndexOf(queueIndex);
            if (orderPos >= 0)
            {
                _orderIndex = orderPos;
            }
            else
            {
                RebuildOrder(queueIndex);
            }
            _isPlayingPlaylist = true;
            track = CurrentTrack;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (track != null && PlayTrackHandler != null)
        {
            return await PlayTrackHandler(track).ConfigureAwait(false);
        }
        return false;
    }

    public async Task<bool> PlayNextAsync()
    {
        PlaylistItem? track;
        lock (_gate)
        {
            if (_queue.Count == 0) return false;
            if (_orderIndex + 1 < _playbackOrder.Count)
            {
                _orderIndex++;
            }
            else if (_repeatMode == PlaylistRepeatMode.All)
            {
                RebuildOrder(0);
            }
            else
            {
                _isPlayingPlaylist = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }
            _isPlayingPlaylist = true;
            track = CurrentTrack;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (track != null && PlayTrackHandler != null)
        {
            return await PlayTrackHandler(track).ConfigureAwait(false);
        }
        return false;
    }

    public async Task<bool> PlayPreviousAsync()
    {
        PlaylistItem? track;
        lock (_gate)
        {
            if (_queue.Count == 0) return false;
            if (_orderIndex > 0)
            {
                _orderIndex--;
            }
            else if (_repeatMode == PlaylistRepeatMode.All)
            {
                _orderIndex = _playbackOrder.Count - 1;
            }
            _isPlayingPlaylist = true;
            track = CurrentTrack;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (track != null && PlayTrackHandler != null)
        {
            return await PlayTrackHandler(track).ConfigureAwait(false);
        }
        return false;
    }

    public async Task OnSongPlaybackCompletedAsync()
    {
        bool shouldRepeatOne = false;
        PlaylistItem? trackToRepeat = null;

        lock (_gate)
        {
            if (!_isPlayingPlaylist || _queue.Count == 0) return;

            if (_repeatMode == PlaylistRepeatMode.One)
            {
                shouldRepeatOne = true;
                trackToRepeat = CurrentTrack;
            }
        }

        if (shouldRepeatOne && trackToRepeat != null && PlayTrackHandler != null)
        {
            await PlayTrackHandler(trackToRepeat).ConfigureAwait(false);
            return;
        }

        await PlayNextAsync().ConfigureAwait(false);
    }

    public void AddToQueue(PlaylistItem item, bool playNext = false)
    {
        lock (_gate)
        {
            if (playNext && _orderIndex >= 0 && _orderIndex < _playbackOrder.Count)
            {
                _queue.Add(item);
                int newQIdx = _queue.Count - 1;
                _playbackOrder.Insert(_orderIndex + 1, newQIdx);
            }
            else
            {
                _queue.Add(item);
                int newQIdx = _queue.Count - 1;
                _playbackOrder.Add(newQIdx);
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFromQueue(string itemId)
    {
        lock (_gate)
        {
            int qIdx = _queue.FindIndex(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (qIdx < 0) return;

            _queue.RemoveAt(qIdx);
            int currentQ = (_orderIndex >= 0 && _orderIndex < _playbackOrder.Count) ? _playbackOrder[_orderIndex] : -1;
            RebuildOrder(currentQ >= 0 && currentQ < _queue.Count ? currentQ : 0);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearQueue()
    {
        lock (_gate)
        {
            _queue.Clear();
            _playbackOrder.Clear();
            _orderIndex = -1;
            _isPlayingPlaylist = false;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
