using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Keynote.App;
using Keynote.Core;
using Keynote.Midi;
using Xunit;

namespace Keynote.Overlay.Tests
{
    [Collection("STA")]
    public class MainWindowScreenshotCaptureTests
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
                    if (Application.Current == null)
                    {
                        var app = new Keynote.App.App();
                        app.InitializeComponent();
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

        private static void CaptureConfiguredWindow(Action<MainWindow> configure, string fileName, string? aliasFileName = null)
        {
            RunInSta(() =>
            {
                var window = new MainWindow();
                try
                {
                    window.Width = 1536;
                    window.Height = 1024;
                    window.Left = -20000;
                    window.Top = -20000;
                    window.Show();

                    configure(window);

                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);

                    window.UpdateLayout();

                    int w = (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : 1536);
                    int h = (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : 1024);

                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(window);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    var filePath = Path.Combine(ArtifactDir, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    using (var stream = File.Create(filePath))
                    {
                        encoder.Save(stream);
                    }
                    Assert.True(File.Exists(filePath), $"Screenshot {fileName} was not created");

                    if (!string.IsNullOrEmpty(aliasFileName))
                    {
                        var aliasPath = Path.Combine(ArtifactDir, aliasFileName);
                        File.Copy(filePath, aliasPath, true);
                    }

                    window.Hide();
                }
                finally
                {
                    window.Dispose();
                }
            });
        }

        [Fact]
        public void Capture_MainWindow_EmptyState_1536x1024()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(0);
            }, "mainwindow_empty.png", "mainwindow_reconstructed.png");
        }

        [Fact]
        public void Capture_MainWindow_LoadedSong_1536x1024()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(0);
                var notes1 = new List<MidiNote>();
                var notes2 = new List<MidiNote>();
                int[] pitches = { 60, 62, 64, 65, 67, 69, 71, 72 };
                for (int i = 0; i < 80; i++)
                {
                    notes1.Add(new MidiNote(pitches[i % pitches.Length], 90, TimeSpan.FromSeconds(i * 0.5), TimeSpan.FromSeconds(0.45), 1));
                    notes2.Add(new MidiNote(pitches[(i + 4) % pitches.Length] - 12, 80, TimeSpan.FromSeconds(i * 0.5), TimeSpan.FromSeconds(0.45), 2));
                }
                var track1 = new MidiTrack(1, notes1);
                var track2 = new MidiTrack(2, notes2);
                var song = new MidiSong("Cornfield Chase", new[] { track1, track2 });

                window.ApplyPlayerSong(song, null);
            }, "mainwindow_loaded.png", "mainwindow_loaded_reconstructed.png");
        }

        [Fact]
        public void Capture_MainWindow_Tab_Discover()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(1);
            }, "tab_discover.png");
        }

        [Fact]
        public void Capture_MainWindow_Tab_Extract()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(2);
            }, "tab_extract.png");
        }

        [Fact]
        public void Capture_MainWindow_Tab_Library()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(3);
            }, "tab_library.png");
        }

        [Fact]
        public void Capture_MainWindow_Tab_Mapping()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(4);
            }, "tab_mapping.png");
        }

        [Fact]
        public void Capture_MainWindow_Tab_Settings()
        {
            CaptureConfiguredWindow(window =>
            {
                window.SelectTab(5);
            }, "tab_settings.png");
        }
    }
}
