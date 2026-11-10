using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Keynote.Core;
using Keynote.Midi;
using Keynote.Playback;
using Xunit;

namespace Keynote.Tests;

public class AllSongsPlayabilityTest
{
    [Fact]
    public void VerifyEverySeededSongIsPlayableInPlaybackEngine()
    {
        var seedDir = Path.Combine(AppContext.BaseDirectory, "RemappedSeed");
        var manifestPath = Path.Combine(seedDir, "manifest.json");
        Assert.True(File.Exists(manifestPath), $"Manifest not found at {manifestPath}");

        var manifestJson = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(manifestJson);
        var songs = doc.RootElement.EnumerateArray().ToArray();

        var profilePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "visual-pianos-c2-unverified.json");
        Assert.True(File.Exists(profilePath), $"Profile not found at {profilePath}");
        var profile = JsonSerializer.Deserialize<KeyboardMapping>(File.ReadAllText(profilePath))!;

        Assert.True(songs.Length >= 395, $"Expected at least 395 songs, got {songs.Length}");

        var importer = new MidiImporter();
        int tested = 0;
        int playable = 0;

        foreach (var item in songs)
        {
            var sha = item.GetProperty("sha256").GetString()!;
            var title = item.GetProperty("title").GetString()!;
            var artist = item.GetProperty("artist").GetString()!;
            var octave = item.TryGetProperty("octave", out var octProp) ? octProp.GetInt32() : 0;

            var midiPath = Path.Combine(seedDir, $"{sha}.mid");
            Assert.True(File.Exists(midiPath), $"MIDI file missing: {midiPath} for {artist} - {title}");

            var bytes = File.ReadAllBytes(midiPath);
            var computedSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(sha, computedSha);

            var song = importer.Import(midiPath);
            Assert.True(song.Duration >= TimeSpan.FromSeconds(60), $"{title} duration is under 60 seconds: {song.Duration.TotalSeconds}s");
            Assert.True(song.Notes.Any(), $"{title} has no notes");

            var missing = profile.Missing(song, octave);
            Assert.Empty(missing);

            var timelineEvents = PlaybackEngine.BuildEvents(song, profile, octave);
            Assert.NotEmpty(timelineEvents);

            tested++;
            playable++;
        }

        Assert.Equal(songs.Length, tested);
        Assert.Equal(tested, playable);
    }

    [Fact]
    public void VerifyEveryModernPickShaExistsAndIsPlayable()
    {
        var seedDir = Path.Combine(AppContext.BaseDirectory, "RemappedSeed");
        var profilePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "visual-pianos-c2-unverified.json");
        var profile = JsonSerializer.Deserialize<KeyboardMapping>(File.ReadAllText(profilePath))!;
        var importer = new MidiImporter();

        // Read MainWindow.Modern.cs source to find all ModernPick definitions
        var modernCsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Keynote.App", "MainWindow.Modern.cs"));
        Assert.True(File.Exists(modernCsPath), $"MainWindow.Modern.cs not found at {modernCsPath}");

        var lines = File.ReadAllLines(modernCsPath);
        int pickCount = 0;

        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith("new(\"")) continue;
            // Parse sha256 from line: 64 hex characters
            var tokens = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
            var sha = tokens.FirstOrDefault(t => t.Length == 64 && t.All(Uri.IsHexDigit));
            if (sha == null) continue;

            var midiPath = Path.Combine(seedDir, $"{sha}.mid");
            Assert.True(File.Exists(midiPath), $"MIDI file missing for ModernPick SHA {sha}");

            var song = importer.Import(midiPath);
            Assert.True(song.Duration >= TimeSpan.FromSeconds(60), $"Song {sha} duration under 60s");
            Assert.Empty(profile.Missing(song, 0));

            var events = PlaybackEngine.BuildEvents(song, profile, 0);
            Assert.NotEmpty(events);
            pickCount++;
        }

        Assert.True(pickCount >= 300, $"Expected >= 300 picks, got {pickCount}");
    }
}
