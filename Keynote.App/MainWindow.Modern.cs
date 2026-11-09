using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Keynote.Core;
using Keynote.Online;

namespace Keynote.App;

public partial class MainWindow
{
    sealed record ModernPick(
        string Artist,
        string Title,
        string Category = "General",
        double DurationSeconds = 0,
        int NoteCount = 0,
        string Sha256 = "",
        RemappedSong? CloudSong = null,
        Uri? Source = null)
    {
        public override string ToString() =>
            $"{Title}  •  {Artist}" +
            (CloudSong != null ? $"  •  {Category}" : ModernDownloads.Find(Artist, Title) != null ? "  •  Creator" : Source != null ? "  •  Source website" : "  •  Search only");
    }

    RemappedCatalogService? modernCloudCatalog;
    IReadOnlyList<RemappedSong> modernCloudSongs = Array.Empty<RemappedSong>();
    CancellationTokenSource? modernDownloadCancellation;
    bool modernCloudLoaded;

    static readonly ModernPick[] ModernPicks = {
        new("Hans Zimmer", "Cornfield Chase (Interstellar)", Source: new Uri("https://www.youtube.com/watch?v=57SdNVHYdNk")),
        new("Kanye West", "Runaway"), new("Kanye West", "I Wonder"), new("Kanye West", "Heartless"), new("Kanye West", "Flashing Lights"), new("Kanye West", "Ghost Town"), new("Kanye West", "Violent Crimes"), new("Kanye West", "Hey Mama"),
        new("Kanye West", "Jail"),
        new("Kanye West", "Pure Souls"),
        new("Lana Del Rey", "The Grants"),
        new("Lana Del Rey", "Candy Necklace"),
        new("Clairo", "Bags"), new("Clairo", "Sofia"), new("Clairo", "Pretty Girl"), new("Clairo", "Amoeba"), new("Clairo", "Bubble Gum"), new("Clairo", "Juna"), new("Clairo", "Sexy to Someone"),
        new("Laufey", "From the Start"), new("Laufey", "Valentine"), new("Laufey", "Promise"), new("Laufey", "Let You Break My Heart Again"), new("Laufey", "Falling Behind"), new("Laufey", "Bewitched"), new("Laufey", "Like the Movies"),
        new("The Weeknd", "Blinding Lights"), new("The Weeknd", "Die for You"), new("The Weeknd", "Save Your Tears"), new("The Weeknd", "Starboy"), new("The Weeknd", "Call Out My Name"), new("The Weeknd", "The Hills"), new("The Weeknd", "Out of Time"),
        new("Frank Ocean", "Pink + White"), new("Frank Ocean", "Ivy"), new("Frank Ocean", "Thinkin Bout You"), new("Tyler, The Creator", "See You Again"), new("Tyler, The Creator", "EARFQUAKE"), new("Steve Lacy", "Bad Habit"), new("Steve Lacy", "Dark Red"), new("SZA", "Snooze"), new("SZA", "Good Days"), new("Billie Eilish", "BIRDS OF A FEATHER"), new("Billie Eilish", "WILDFLOWER"), new("Lana Del Rey", "Young and Beautiful"), new("Lana Del Rey", "Summertime Sadness"), new("Joji", "Glimpse of Us"), new("Joji", "SLOW DANCING IN THE DARK"), new("Daniel Caesar", "Best Part"), new("Mac DeMarco", "Chamber of Reflection")
    };

    void InitializeModernPicks()
    {
        ApplyModernFilter();
        if (!WorkflowMode && !Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _ = LoadModernCloudSongsAsync();
        }
    }

    async Task LoadModernCloudSongsAsync()
    {
        if (onlineHttp == null || downloads == null) return;
        try
        {
            if (ModernStatusText != null)
                ModernStatusText.Text = "Loading cloud library…";

            modernCloudCatalog ??= new RemappedCatalogService(onlineHttp, downloads);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var songs = await modernCloudCatalog.ListAsync(cts.Token).ConfigureAwait(true);
            if (songs != null && songs.Count > 0)
            {
                modernCloudSongs = songs;
                modernCloudLoaded = true;
                if (ModernStatusText != null)
                    ModernStatusText.Text = $"{songs.Count} cloud songs connected · Visual Pianos (C2–C7)";
                ApplyModernFilter();
            }
        }
        catch
        {
            if (ModernStatusText != null)
                ModernStatusText.Text = modernCloudLoaded ? $"{modernCloudSongs.Count} cloud songs available" : "Cloud library ready.";
        }
    }

    void OnCategoryFilterChanged(object sender, RoutedEventArgs e) => ApplyModernFilter();
    void ModernFilterChanged(object sender, TextChangedEventArgs e) => ApplyModernFilter();

    string GetSelectedCategory()
    {
        if (CatPop?.IsChecked == true) return "Pop";
        if (CatClassical?.IsChecked == true) return "Classical";
        if (CatAnimeGames?.IsChecked == true) return "Anime & Games";
        if (CatFilmTV?.IsChecked == true) return "Film & TV";
        if (CatTraditional?.IsChecked == true) return "Traditional";
        return "All";
    }

    void ApplyModernFilter()
    {
        if (ModernList == null || ModernQuery == null) return;
        var q = ModernQuery.Text.Trim();
        var cat = GetSelectedCategory();

        // 1. Cloud library songs (authoritative, pre-remapped)
        var cloudPicks = modernCloudSongs.Select(s => new ModernPick(
            s.Artist,
            s.Title,
            string.IsNullOrWhiteSpace(s.Category) ? "Classical" : s.Category,
            s.DurationSeconds,
            s.KeptNotes,
            s.Sha256,
            s,
            Uri.TryCreate(s.Source, UriKind.Absolute, out var u) ? u : null
        ));

        // 2. Direct creator downloads
        var directPicks = ModernDownloads.Available.Select(x => new ModernPick(
            x.Artist,
            x.Title,
            "Pop",
            Sha256: x.Sha256,
            Source: Uri.TryCreate(x.Source, UriKind.Absolute, out var su) ? su : null
        ));

        // In WorkflowMode or offline before cloud loads, show default shortlist
        var basePicks = (modernCloudLoaded && modernCloudSongs.Count > 0)
            ? cloudPicks.Concat(directPicks).Concat(ModernPicks)
            : ModernPicks.Select(p => p with {
                CloudSong = modernCloudSongs.FirstOrDefault(s => s.Artist.Equals(p.Artist, StringComparison.OrdinalIgnoreCase) && s.Title.Equals(p.Title, StringComparison.OrdinalIgnoreCase))
            });

        var filtered = basePicks
            .Where(p => cat == "All" || p.Category.Equals(cat, StringComparison.OrdinalIgnoreCase) || (cat == "Pop" && p.Category.Contains("Pop", StringComparison.OrdinalIgnoreCase)))
            .Where(p => string.IsNullOrWhiteSpace(q) ||
                        p.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => !string.IsNullOrEmpty(p.Sha256) ? p.Sha256 : $"{p.Artist.ToUpperInvariant()}\0{p.Title.ToUpperInvariant()}")
            .ToArray();

        ModernList.ItemsSource = filtered;
        if (ModernStatusText != null && modernCloudLoaded)
        {
            ModernStatusText.Text = $"{filtered.Length} arrangements shown · {cat} · Visual Pianos C2–C7";
        }
    }

    LibraryEntry? FindLocalModern(ModernPick pick)
    {
        if (!string.IsNullOrEmpty(pick.Sha256))
        {
            var match = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, pick.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        if (pick.CloudSong != null)
        {
            var match = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, pick.CloudSong.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        var direct = ModernDownloads.Find(pick.Artist, pick.Title);
        if (direct != null)
        {
            var match = libraryEntries.FirstOrDefault(e => string.Equals(e.Sha256, direct.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return libraryEntries.FirstOrDefault(e =>
            e.Artist.Equals(pick.Artist, StringComparison.OrdinalIgnoreCase) &&
            (e.Title.Equals(pick.Title, StringComparison.OrdinalIgnoreCase) || e.Title.StartsWith(pick.Title + " ", StringComparison.OrdinalIgnoreCase)));
    }

    void ModernSelected(object sender, SelectionChangedEventArgs e)
    {
        modernDownloadCancellation?.Cancel();
        if (ModernDetails == null || ModernSearchButton == null || ModernLibraryButton == null || ModernDownloadButton == null) return;
        var pick = ModernList.SelectedItem as ModernPick;
        ModernSearchButton.IsEnabled = pick != null;
        ModernLibraryButton.IsEnabled = pick != null;

        if (pick == null)
        {
            ModernDownloadButton.IsEnabled = false;
            ModernDetails.Text = "Select an arrangement to view calibration details and play.";
            return;
        }

        var local = FindLocalModern(pick);
        var direct = ModernDownloads.Find(pick.Artist, pick.Title);
        var isLocal = local != null;
        ModernDownloadButton.IsEnabled = isLocal || pick.CloudSong != null || direct != null;
        ModernDownloadButton.Content = isLocal ? "Play from Library" : "Download and Play";

        var durationStr = pick.DurationSeconds > 0 ? $"{TimeSpan.FromSeconds(pick.DurationSeconds):m\\:ss}" : "Standard";
        var notesStr = pick.NoteCount > 0 ? $"{pick.NoteCount} notes" : "Full score";
        var shaDisplay = !string.IsNullOrEmpty(pick.Sha256) && pick.Sha256.Length >= 12
            ? $"{pick.Sha256[..8]}…{pick.Sha256[^4..]}"
            : "SHA-256 Verified";

        ModernDetails.Text =
            $"{pick.Title}\n{pick.Artist}\n\n" +
            $"Category: {pick.Category}\n" +
            $"Duration: {durationStr}  •  Notes: {notesStr}\n" +
            $"Target Piano: Visual Pianos (C2–C7, 61 keys)\n" +
            $"Checksum: {shaDisplay}\n\n" +
            (isLocal
                ? "✓ This arrangement is downloaded and ready in your local library. Click 'Play from Library' to start."
                : pick.CloudSong != null
                    ? "☁ Ready for one-click cloud download and Roblox playback. Pre-calibrated; no pitch adjustment required."
                    : direct != null
                        ? "A free original arrangement is available from its creator. Check your piano mapping and adjust any unmapped notes before playing."
                        : pick.Source != null
                            ? "The creator offers a free MIDI link in the video description. Download it, then use Browse MIDI."
                            : "Direct download is unavailable for this title. Search MIDI sources or import your own copy.");
    }

    async void DownloadModern(object sender, RoutedEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick pick || library == null || downloads == null) return;
        modernDownloadCancellation?.Cancel();
        var request = new CancellationTokenSource();
        modernDownloadCancellation = request;
        ModernDownloadButton.IsEnabled = false;

        try
        {
            var local = FindLocalModern(pick);
            if (local == null)
            {
                ModernDetails.Text = "Downloading and verifying MIDI from cloud storage…";
                var progress = new Progress<double>(p =>
                {
                    if (ReferenceEquals(modernDownloadCancellation, request))
                        ModernDetails.Text = $"Downloading {p:P0}…";
                });

                if (pick.CloudSong != null)
                {
                    modernCloudCatalog ??= new RemappedCatalogService(onlineHttp ?? MidiDownloadService.CreateClient(), downloads);
                    var saved = await modernCloudCatalog.DownloadAsync(pick.CloudSong, library, progress, request.Token).ConfigureAwait(true);
                    request.Token.ThrowIfCancellationRequested();
                    await ReloadLibrary(request.Token).ConfigureAwait(true);
                    local = libraryEntries.FirstOrDefault(x => string.Equals(x.Sha256, saved.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException("Downloaded song was not found in your local library.");
                }
                else
                {
                    var direct = ModernDownloads.Find(pick.Artist, pick.Title);
                    if (direct != null)
                    {
                        var saved = await new ModernDownloads(downloads, library).DownloadAsync(direct, progress, request.Token).ConfigureAwait(true);
                        request.Token.ThrowIfCancellationRequested();
                        await ReloadLibrary(request.Token).ConfigureAwait(true);
                        local = libraryEntries.FirstOrDefault(x => string.Equals(x.Sha256, saved.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidOperationException("Downloaded song was not found in your local library.");
                    }
                    else
                    {
                        throw new InvalidOperationException("Direct cloud download is not yet staged for this title. Search online or import a local copy.");
                    }
                }
            }

            request.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(ModernList.SelectedItem, pick)) return;

            // Load into PlayerSongSelector and switch to Tab 0 (Playback)
            if (ReferenceEquals(PlayerSongSelector.SelectedItem, local))
            {
                await LoadLibrarySelection(local, () => PlayerSongSelector.SelectedItem as LibraryEntry).ConfigureAwait(true);
            }
            else
            {
                PlayerSongSelector.SelectedItem = local;
            }
            MainTabs.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(modernDownloadCancellation, request))
                ModernDetails.Text = "Could not download or play this song: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(modernDownloadCancellation, request))
            {
                modernDownloadCancellation = null;
                var selected = ModernList.SelectedItem as ModernPick;
                ModernDownloadButton.IsEnabled = selected != null;
            }
            request.Dispose();
        }
    }

    async void SearchModern(object sender, RoutedEventArgs e) => await SearchModernAsync();

    async Task SearchModernAsync()
    {
        if (ModernList.SelectedItem is not ModernPick pick) return;
        if (pick.Source != null && pick.CloudSong == null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pick.Source.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ModernDetails.Text = "Could not open MIDI source: " + ex.Message;
            }
            return;
        }
        MainTabs.SelectedIndex = 1;
        CatalogSelector.SelectedIndex = 0;
        SearchQuery.Text = $"{pick.Artist} {pick.Title}";
        searchDebouncer?.Cancel();
        await SearchOnlineAsync(0, true, CancellationToken.None).ConfigureAwait(true);
    }

    void FindModernInLibrary(object sender, RoutedEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick pick) return;
        LibraryQuery.Text = pick.Title;
        MainTabs.SelectedIndex = 3;
    }

    void ModernDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ModernList.SelectedItem is not ModernPick) return;
        DownloadModern(sender, e);
    }
}
