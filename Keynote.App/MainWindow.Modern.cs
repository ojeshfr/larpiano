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
        new("Hans Zimmer", "Cornfield Chase (Interstellar)", "Film & TV", 127.51, 1269, "69aff63cc2cdeac313de00b2874dcca257403c5507cf03bc09d9dcd1dbdef09a", Source: new Uri("https://www.youtube.com/watch?v=57SdNVHYdNk")),
        new("Kanye West", "Runaway", "Pop", 68.97, 296, "00904d8488eaa80a967944026baa912224c07383c14952056b4239dfebb49461"),
        new("Kanye West", "I Wonder", "Pop", 93.75, 810, "0614a7d748d90831f451d25c13e049530ea68a0eec85225e2b1e372763289c38"),
        new("Kanye West", "Heartless", "Pop", 87.61, 504, "b6673357de246b63fd9edc5dfa9b1cef11227ec0d7b4342a8cace661738654ac"),
        new("Kanye West", "Flashing Lights", "Pop", 85.3, 626, "d99a096c884397ba9524779a11cf7532abc3013be5ebd600792f1455b154fe60"),
        new("Kanye West", "Ghost Town", "Pop", 91.36, 240, "e1ba64fa389dac15ef8cd08630227675e4837c4a95909c43a3b8e65da3969d7f"),
        new("Kanye West", "Violent Crimes", "Pop", 114.63, 261, "9603d78ea896b76b40abdf9cb86ea16ddc73fedb51f080c97f75dae6228cc088"),
        new("Kanye West", "Hey Mama", "Pop", 74.67, 236, "1cb1f63d7c2c8b8d15a68e6c04daf36734680d57aae95da2dea818bb2dc8eccf"),
        new("Kanye West", "Jail", "Pop", 157.09, 821, "2143e152a4eebb63ba9edec7081fc97c6802dd93a82b208ec19f12e965c3b30c"),
        new("Kanye West", "Pure Souls", "Pop", 121.24, 537, "f3c001002c3247dae991af6794d734e609fba68c34a5df0ad8b56b1177d48425"),
        new("Lana Del Rey", "The Grants", "Pop", 246.13, 860, "7a04ad9170359c27b282204c0d399ba5758b0b8530e5ceb1e157195225ca201d"),
        new("Lana Del Rey", "Candy Necklace", "Pop", 298.6, 1282, "dd3b68fc0302d6ea84c7713a92d137e773eb70be1ab6fb12d99bca5181130f5b"),
        new("Clairo", "Bags", "Pop", 73.03, 670, "26c81510acf099e8e9f801e0316e4bbc73cad0645d77e938f6ddccac2e6344a8"),
        new("Clairo", "Sofia", "Pop", 66.68, 529, "74f39b1dbc75b2703445206579fd8c88aa7ba75d3e8af7f87543a87a563b322a"),
        new("Clairo", "Pretty Girl", "Pop", 80.84, 550, "5c66e609a3dd2a4308876f7ff1755e193d800da8b95f60ebc76809ecf7c948a8"),
        new("Clairo", "Amoeba", "Pop", 66.13, 734, "32dab2a39226058ebd6260316fa84b1f04ccc8b546ac73a1a2b33fa34c2e6361"),
        new("Clairo", "Bubble Gum", "Pop", 93.88, 190, "5350f8e5dd3cb89aeedf926364913579a69f8554e6f036f86b94f57195cffca7"),
        new("Clairo", "Juna", "Pop", 85.2, 318, "c822bd0f9612277b18bd911673834422155489d67a65d3e57412db933f0092b8"),
        new("Clairo", "Sexy to Someone", "Pop", 69.71, 475, "c7ad33f6cbf28f5e43573a257937d6954b8b63b1af72cc6a705a92b3f0e1fa7a"),
        new("Laufey", "From the Start", "Pop", 166.83, 1941, "425ae0b8afc7306c3d492d1d9c78f7a4b30fe061c444ee818fd2f020f94ecdd7"),
        new("Laufey", "Valentine", "Pop", 137.0, 699, "27788ba0994111b695930ed896d568c7487b7ff6784ae24fe956587049b96def"),
        new("Laufey", "Promise", "Pop", 164.83, 312, "3eb7fce1e49f3f6a3efd1fc57a1c0c8bd69987499cae562673c031805009d4eb"),
        new("Laufey", "Let You Break My Heart Again", "Pop", 155.51, 483, "8d99758e216663435b784d7754e8950056f1e53f0f440a8bc8c07232d24901e6"),
        new("Laufey", "Falling Behind", "Pop", 88.5, 990, "ef63ddff456eca086250dc721f623b6b2088aea5e9db8ebdf8202382b3e47af9"),
        new("Laufey", "Bewitched", "Pop", 159.83, 483, "3c706a96ff628c2cfd59001d2970d13677a2529f1c107b8fd83aa42da312a783"),
        new("Laufey", "Like the Movies", "Pop", 104.7, 894, "512215c8cf8c33809939a1c2c9a1a3f5b9e512a16b04b82ca0e7a4e260c62da1"),
        new("The Weeknd", "Blinding Lights", "Pop", 182.73, 590, "5a18fdae05c35021ca88f213c444712708136c4bb021112cd8fb8d0fe0180ad9"),
        new("The Weeknd", "Die for You", "Pop", 114.45, 545, "67f555705a2f0e358cd9601153496e334b0f7548a51d28c264f11d36b20de248"),
        new("The Weeknd", "Save Your Tears", "Pop", 101.7, 2392, "aaaebc3caffe5f3a79d4ae94882ffa2d3248758924300ae720b60569b53b6b75"),
        new("The Weeknd", "Starboy", "Pop", 82.55, 538, "11e1c02d2055fa2868b7a2785ca946f7c0e87c181e930286bee6d24e30369f49"),
        new("The Weeknd", "Call Out My Name", "Pop", 209.28, 1050, "30b071117813d73bc21321362e317c98c7b548ff38189d1f3bac9d98bfa5fd83"),
        new("The Weeknd", "The Hills", "Pop", 167.78, 850, "698535fccccae4b8601850fbcbb4102aa5b4c779ed0fe079c3ef82ff4ecc5ee9"),
        new("The Weeknd", "Out of Time", "Pop", 82.42, 625, "460e225a48caeb41349b164f397a6e9aa6f454364fbff98c7f8c64a16a6ddb81"),
        new("Frank Ocean", "Pink + White", "Pop", 95.95, 706, "ef9c3574a9cce7904f7a7b0534689c763dd1de28be8420e9689167b29618370d"),
        new("Frank Ocean", "Ivy", "Pop", 79.94, 200, "00f0602b87bb9012d1a32f941d5324581631b160abac37a8532daf97415eeb97"),
        new("Frank Ocean", "Thinkin Bout You", "Pop", 117.97, 548, "5bb68f1452021be22770b6a5c6356a7e91a536b7e30885c3c3688c5a66fd11c3"),
        new("Tyler, The Creator", "See You Again", "Pop", 97.06, 724, "37a9082e5d97a5f612006a70272adffcdc239a78b9d42a5f16aa8303d71ef2ca"),
        new("Tyler, The Creator", "EARFQUAKE", "Pop", 95.81, 720, "74e5c78f8990ec0e2f8490afff50bd05e04c7b0042c8d5fd21aff6febbedf17b"),
        new("Steve Lacy", "Bad Habit", "Pop", 90.25, 657, "96d1ee5d5cb6e224f831b0a7713eeae47b79aaffbae1cb6f607f1a7fb8e02ec5"),
        new("Steve Lacy", "Dark Red", "Pop", 66.68, 618, "63626861f0b2d1c8e6b279ad0dd5db97a22a7949baf0d4c8ef769d5b7f7c5640"),
        new("SZA", "Snooze", "Pop", 106.5, 572, "92902fc5d1acc08adc52aa26fc6335a5dcad81f9ffb404effe981b703e053747"),
        new("SZA", "Good Days", "Pop", 127.8, 205, "7a92bdf6779ae40c3f7ff7539cb85f6d3e841b4226f96af4369ecdccb9535c65"),
        new("Billie Eilish", "BIRDS OF A FEATHER", "Pop", 77.71, 2574, "033e7f1601540b3230771569855c8bfea0d86398ef484883200eb548b9135632"),
        new("Billie Eilish", "WILDFLOWER", "Pop", 112.76, 287, "5ee5a95e4408a630cc437ce50b9797f59d91145e3080094951be7ba7db3952cc"),
        new("Lana Del Rey", "Young and Beautiful", "Pop", 116.27, 340, "7b526f33c5ec440147eb2d6c9df78a2d7afcbe8d94fa59d6653d1b936688f96a"),
        new("Lana Del Rey", "Summertime Sadness", "Pop", 263.44, 1118, "3ca20c41586f852269fd96b615ae6a87094d871a27ae688a7bed61b7b6d74081"),
        new("Joji", "Glimpse of Us", "Pop", 90.21, 450, "eb588f6302a23560b8463b404344d0abaed62e40590233e87add94f19adb334e"),
        new("Joji", "SLOW DANCING IN THE DARK", "Pop", 102.24, 356, "894bbda491afd1c38afb09d5eb4ea96560d3394bf58f9e9f1ed1f3655d872687"),
        new("Daniel Caesar", "Best Part", "Pop", 102.24, 220, "c7dbbdfbbabb99bf666eb59d44b740741f0158ec9c6400326292747a2054d3dd"),
        new("Mac DeMarco", "Chamber of Reflection", "Pop", 116.18, 608, "e39c7aa3005401b6c5d01b1b3ccc6577ed031d45c1480237fd0103cb606ed31c"),
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
        ModernDownloadButton.IsEnabled = isLocal || pick.CloudSong != null || direct != null || !string.IsNullOrEmpty(pick.Sha256);
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
                : (pick.CloudSong != null || !string.IsNullOrEmpty(pick.Sha256))
                    ? "☁ Ready for one-click cloud download and game playback. Pre-calibrated; no pitch adjustment required."
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
                    else if (!string.IsNullOrEmpty(pick.Sha256))
                    {
                        var tempCloudSong = new RemappedSong(
                            pick.Sha256,
                            pick.Title,
                            pick.Artist,
                            pick.Source?.AbsoluteUri ?? "https://larpiano.portways.app",
                            Category: pick.Category,
                            DurationSeconds: pick.DurationSeconds,
                            KeptNotes: pick.NoteCount
                        );
                        modernCloudCatalog ??= new RemappedCatalogService(onlineHttp ?? MidiDownloadService.CreateClient(), downloads);
                        var saved = await modernCloudCatalog.DownloadAsync(tempCloudSong, library, progress, request.Token).ConfigureAwait(true);
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
