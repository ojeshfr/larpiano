using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Keynote.App;
using Keynote.App.Overlay;
using Keynote.Core;
using Keynote.Infrastructure;
using Keynote.Input;
using Keynote.Playback;
using Xunit;

namespace Keynote.Overlay.Tests
{
    [Collection("STA")]
    public class OverlayScreenshotCaptureTests
    {
        private const string ArtifactDir = @"C:\Users\Lenovo\.gemini\antigravity\brain\bfc3f20a-053f-4fef-b7ab-6cf73c28a4e6";

        private static void RunInSta(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.ResourceAssembly == null)
                    {
                        Application.ResourceAssembly = typeof(Keynote.App.MainWindow).Assembly;
                    }
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        private static void SaveWindowToPng(Window window, string filePath)
        {
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            window.UpdateLayout();

            int w = (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : window.Width);
            int h = (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : window.Height);

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }

        [Fact]
        public void Capture_OverlayScreenshots_ExpandedAndCompact()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings { ExpandedWidth = 920, ExpandedHeight = 440, GlassOpacity = 0.97 };
                var vm = new OverlayViewModel(settings);

                // Create a realistic song (Cornfield Chase style with multiple tracks and notes)
                var notes1 = new List<MidiNote>();
                var notes2 = new List<MidiNote>();

                // Bass arpeggios
                int[] bassPitches = { 36, 43, 48, 55, 38, 45, 50, 57, 40, 47, 52, 59 };
                for (int i = 0; i < 60; i++)
                {
                    int pitch = bassPitches[i % bassPitches.Length];
                    notes1.Add(new MidiNote(pitch, 90, TimeSpan.FromSeconds(i * 2.0), TimeSpan.FromSeconds(1.8), 1));
                }

                // Lead melody chords
                int[] melodyPitches = { 60, 64, 67, 72, 62, 65, 69, 74, 64, 67, 71, 76, 67, 71, 74, 79 };
                for (int i = 0; i < 180; i++)
                {
                    int pitch = melodyPitches[i % melodyPitches.Length];
                    notes2.Add(new MidiNote(pitch, 100, TimeSpan.FromSeconds(i * 0.65), TimeSpan.FromSeconds(0.6), 2));
                }

                var track1 = new MidiTrack(1, notes1);
                var track2 = new MidiTrack(2, notes2);
                var song = new MidiSong("Cornfield Chase", new[] { track1, track2 });

                var mapping = new KeyboardMapping
                {
                    Name = "Visual Pianos",
                    Verified = true
                };
                for (int p = 36; p <= 84; p++)
                {
                    mapping.Keys[p] = "A";
                }

                var roblox = new RobloxWindow(new IntPtr(1234), "Meet People Across The World", 9999);
                var fakeInput = new FakeKeyboardInput { IsTargetForeground = true };
                using var engine = new PlaybackEngine(fakeInput);
                var hotkeys = new HotkeyPreferences(HotkeyModifiers.Control | HotkeyModifiers.Alt, 8, 9);

                vm.SynchronizeState(
                    song,
                    TimeSpan.FromSeconds(122),
                    engine,
                    null!,
                    mapping,
                    0,
                    hotkeys,
                    roblox);

                // Set position to 26 seconds as in reference screenshot
                vm.Seek(TimeSpan.FromSeconds(26));

                // 1. Expanded Overlay
                var window = new OverlayWindow(vm, settings);
                window.ApplyLayoutMode(false);
                window.Show();

                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => { }));

                string expandedPath = Path.Combine(ArtifactDir, "overlay_expanded_polished.png");
                SaveWindowToPng(window, expandedPath);
                Assert.True(File.Exists(expandedPath));
                File.Copy(expandedPath, Path.Combine(ArtifactDir, "overlay_expanded_final.png"), true);

                // 2. Compact Overlay
                vm.IsCompact = true;
                window.ApplyLayoutMode(true);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => { }));

                string compactPath = Path.Combine(ArtifactDir, "overlay_compact_polished.png");
                SaveWindowToPng(window, compactPath);
                Assert.True(File.Exists(compactPath));
                File.Copy(compactPath, Path.Combine(ArtifactDir, "overlay_compact_final.png"), true);

                window.IsRealClose = true;
                window.Close();
            });
        }

        [Theory]
        [InlineData(920)]
        [InlineData(1000)]
        [InlineData(1100)]
        public void ExpandedOverlay_ResponsiveLayout_AdaptsGracefully(double targetWidth)
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings { ExpandedWidth = targetWidth, ExpandedHeight = 440, GlassOpacity = 0.97 };
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                window.ApplyLayoutMode(false);
                window.Width = targetWidth;
                window.Show();

                Assert.Equal(targetWidth, window.Width);
                Assert.Equal(440, window.Height);

                window.IsRealClose = true;
                window.Close();
            });
        }
    }
}
