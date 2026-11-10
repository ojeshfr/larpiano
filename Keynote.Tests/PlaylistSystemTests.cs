using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Keynote.Core;
using Keynote.Infrastructure;
using Xunit;

namespace Keynote.Tests;

public class PlaylistSystemTests : IDisposable
{
    private readonly string _tempFile;

    public PlaylistSystemTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"larpiano_test_playlists_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public async Task LocalPlaylistStore_Lifecycle_Succeeds()
    {
        var store = new LocalPlaylistStore(_tempFile);

        // 1. Create
        var p = await store.CreatePlaylistAsync("Favorites");
        Assert.Equal("Favorites", p.Name);
        Assert.Equal("guest", p.UserId);

        // 2. Add Items
        var it1 = await store.AddItemAsync(p.Id, new string('1', 64), "Song 1", "Artist 1", 120.0);
        Assert.NotNull(it1);
        Assert.Equal(0, it1.OrderIndex);

        var it2 = await store.AddItemAsync(p.Id, new string('2', 64), "Song 2", "Artist 2", 180.0);
        Assert.NotNull(it2);
        Assert.Equal(1, it2.OrderIndex);

        // 3. Read back
        var fetched = await store.GetPlaylistAsync(p.Id);
        Assert.NotNull(fetched);
        Assert.Equal(2, fetched.ItemCount);
        Assert.Equal(300.0, fetched.TotalDuration);
        Assert.Equal("Song 1", fetched.Items![0].Title);
        Assert.Equal("Song 2", fetched.Items[1].Title);

        // 4. Reorder
        var reordered = await store.ReorderItemsAsync(p.Id, new[] { it2.Id, it1.Id });
        Assert.True(reordered);
        var fetchedAfterReorder = await store.GetPlaylistAsync(p.Id);
        Assert.Equal("Song 2", fetchedAfterReorder!.Items![0].Title);
        Assert.Equal("Song 1", fetchedAfterReorder.Items[1].Title);

        // 5. Duplicate
        var dup = await store.DuplicatePlaylistAsync(p.Id, "Favorites Copy");
        Assert.NotNull(dup);
        Assert.Equal("Favorites Copy", dup.Name);
        Assert.Equal(2, dup.ItemCount);

        // 6. Delete
        var del = await store.DeletePlaylistAsync(p.Id);
        Assert.True(del);
        var missing = await store.GetPlaylistAsync(p.Id);
        Assert.Null(missing);

        // Ensure persistence survived into fresh store instance
        var store2 = new LocalPlaylistStore(_tempFile);
        var remaining = await store2.GetPlaylistsAsync();
        Assert.Single(remaining);
        Assert.Equal("Favorites Copy", remaining[0].Name);
    }

    [Fact]
    public async Task PlaylistManager_Sequencing_AutoAdvance_And_Repeat()
    {
        var manager = new PlaylistManager();
        var items = new List<PlaylistItem>
        {
            new("id1", "p1", new string('a', 64), "Track A", "Artist A", 100),
            new("id2", "p1", new string('b', 64), "Track B", "Artist B", 200),
            new("id3", "p1", new string('c', 64), "Track C", "Artist C", 300),
        };
        var playlist = new Playlist("p1", "Album", Items: items, ItemCount: 3);

        var playedTracks = new List<string>();
        manager.PlayTrackHandler = track =>
        {
            playedTracks.Add(track.Title);
            return Task.FromResult(true);
        };

        // Load & Start
        manager.LoadPlaylist(playlist);
        Assert.Equal(3, manager.TotalTracks);
        Assert.Equal(1, manager.CurrentTrackNumber);
        Assert.Equal("Track A", manager.CurrentTrack?.Title);

        var started = await manager.StartPlaylistAsync();
        Assert.True(started);
        Assert.True(manager.IsPlayingPlaylist);
        Assert.Single(playedTracks);
        Assert.Equal("Track A", playedTracks[0]);

        // Next
        await manager.PlayNextAsync();
        Assert.Equal(2, manager.CurrentTrackNumber);
        Assert.Equal("Track B", manager.CurrentTrack?.Title);
        Assert.Equal("Track B", playedTracks.Last());

        // Previous
        await manager.PlayPreviousAsync();
        Assert.Equal(1, manager.CurrentTrackNumber);
        Assert.Equal("Track A", manager.CurrentTrack?.Title);

        // Completion with RepeatMode.None advances to end then stops
        await manager.PlayNextAsync(); // Track B
        await manager.PlayNextAsync(); // Track C
        Assert.Equal(3, manager.CurrentTrackNumber);

        // End reached
        await manager.OnSongPlaybackCompletedAsync();
        Assert.False(manager.IsPlayingPlaylist);

        // Now test RepeatMode.All
        manager.CycleRepeatMode(); // None -> All
        Assert.Equal(PlaylistRepeatMode.All, manager.RepeatMode);

        await manager.PlayTrackAtAsync(2); // Track C
        Assert.Equal("Track C", manager.CurrentTrack?.Title);
        await manager.OnSongPlaybackCompletedAsync(); // Loops to Track A
        Assert.Equal("Track A", manager.CurrentTrack?.Title);

        // Now test RepeatMode.One
        manager.CycleRepeatMode(); // All -> One
        Assert.Equal(PlaylistRepeatMode.One, manager.RepeatMode);
        playedTracks.Clear();
        await manager.OnSongPlaybackCompletedAsync();
        Assert.Equal("Track A", manager.CurrentTrack?.Title);
        Assert.Single(playedTracks);
        Assert.Equal("Track A", playedTracks[0]);
    }

    [Fact]
    public void PlaylistManager_Shuffle_Toggles_And_Maintains_Elements()
    {
        var manager = new PlaylistManager();
        var items = Enumerable.Range(1, 10).Select(i =>
            new PlaylistItem($"id{i}", "p1", new string((char)('a' + i), 64), $"Track {i}", $"Artist {i}", 100)
        ).ToList();
        var playlist = new Playlist("p1", "Shuffle Test", Items: items, ItemCount: 10);

        manager.LoadPlaylist(playlist);
        Assert.False(manager.IsShuffleEnabled);

        manager.ToggleShuffle();
        Assert.True(manager.IsShuffleEnabled);
        Assert.Equal(10, manager.TotalTracks);

        // Queue contains all 10 tracks
        Assert.Equal(10, manager.Queue.Count);

        manager.ToggleShuffle();
        Assert.False(manager.IsShuffleEnabled);
    }
}
