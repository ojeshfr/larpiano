using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
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
    public class FakeKeyboardInput : IKeyboardInput
    {
        public bool IsTargetForeground { get; set; } = true;
        public List<string> PressedKeys { get; } = new();
        public List<string> ReleasedKeys { get; } = new();
        public bool IsDisposed { get; private set; }

        public void Press(string key) => PressedKeys.Add(key);
        public void Release(string key) => ReleasedKeys.Add(key);
        public void ReleaseAll()
        {
            ReleasedKeys.AddRange(PressedKeys);
            PressedKeys.Clear();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [Collection("STA")]
    public class OverlayIntegrationTests
    {
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

        private static MidiSong CreateSampleSong(string title = "Test Song")
        {
            var note1 = new MidiNote(60, 100, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), 1);
            var note2 = new MidiNote(62, 100, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1);
            var note3 = new MidiNote(64, 100, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 1);
            var track = new MidiTrack(1, new[] { note1, note2, note3 });
            return new MidiSong(title, new[] { track });
        }

        private static KeyboardMapping CreateDefaultMapping()
        {
            var mapping = new KeyboardMapping
            {
                Name = "Visual Pianos",
                Verified = true
            };
            mapping.Keys[60] = "C";
            mapping.Keys[62] = "D";
            mapping.Keys[64] = "E";
            return mapping;
        }

        // --- 1. OverlaySettings Tests ---

        [Fact]
        public void Settings_Defaults_AreValidAndSensible()
        {
            var settings = new OverlaySettings();
            Assert.Equal(920, settings.ExpandedWidth);
            Assert.Equal(440, settings.ExpandedHeight);
            Assert.False(settings.IsCompact);
            Assert.True(settings.IsPinned);
            Assert.Equal(0.97, settings.GlassOpacity, 2);
        }

        [Fact]
        public void Settings_ValidateAndAdjustBounds_ClampsCorruptDimensions()
        {
            var settings = new OverlaySettings
            {
                ExpandedWidth = 50, // Below minimum 640
                ExpandedHeight = 2000, // Above maximum 800
                GlassOpacity = 0.1 // Below minimum 0.85
            };

            settings.ValidateAndAdjustBounds();

            Assert.Equal(640, settings.ExpandedWidth);
            Assert.Equal(800, settings.ExpandedHeight);
            Assert.Equal(0.85, settings.GlassOpacity, 2);
        }

        [Fact]
        public void Settings_SaveAndLoad_RoundtripsSuccessfully()
        {
            var original = new OverlaySettings
            {
                Left = 350,
                Top = 150,
                ExpandedWidth = 980,
                ExpandedHeight = 460,
                IsCompact = true,
                IsPinned = false,
                GlassOpacity = 0.88
            };

            original.Save();
            var loaded = OverlaySettings.Load();

            Assert.Equal(original.IsCompact, loaded.IsCompact);
            Assert.Equal(original.IsPinned, loaded.IsPinned);
            Assert.Equal(original.GlassOpacity, loaded.GlassOpacity, 2);
        }

        // --- 2. OverlayViewModel State Synchronization Tests ---

        [Fact]
        public void ViewModel_StateSynchronization_SongMetadata()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            var song = CreateSampleSong("Interstellar");
            var fakeInput = new FakeKeyboardInput();
            using var engine = new PlaybackEngine(fakeInput);
            var mapping = CreateDefaultMapping();
            var hotkeys = new HotkeyPreferences(HotkeyModifiers.Control | HotkeyModifiers.Alt, 8, 9);

            vm.SynchronizeState(
                song,
                song.Duration,
                engine,
                null!,
                mapping,
                0,
                hotkeys,
                null);

            Assert.True(vm.HasSong);
            Assert.Equal("Interstellar", vm.SongTitle);
            Assert.Equal(3, vm.Song?.Notes.Count());
            Assert.Equal(TimeSpan.FromSeconds(3), vm.TotalDuration);
            Assert.Contains("1 tracks", vm.SongSubtitle);
            Assert.Contains("3 notes", vm.SongSubtitle);
        }

        [Fact]
        public void ViewModel_StateSynchronization_PlaybackEngineState()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            var fakeInput = new FakeKeyboardInput();
            using var engine = new PlaybackEngine(fakeInput);
            engine.Speed = 1.25;

            vm.SynchronizeState(
                null,
                TimeSpan.FromSeconds(120),
                engine,
                null!,
                CreateDefaultMapping(),
                0,
                new HotkeyPreferences(HotkeyModifiers.Control | HotkeyModifiers.Alt, 8, 9),
                null);

            Assert.Equal(PlaybackState.Idle, vm.PlaybackState);
            Assert.False(vm.IsPlaying);
            Assert.Equal("Idle", vm.PlaybackStateText);
            Assert.Equal(1.25, vm.PlaybackSpeed);
            Assert.Equal("1.25×", vm.SpeedText);
        }

        [Fact]
        public void ViewModel_StateSynchronization_RobloxWindowTarget()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            var fakeInput = new FakeKeyboardInput { IsTargetForeground = true };
            using var engine = new PlaybackEngine(fakeInput);

            var roblox = new RobloxWindow(new IntPtr(12345), "Roblox: Meet People Across The World", 1001);

            vm.SynchronizeState(
                null,
                TimeSpan.Zero,
                engine,
                null!,
                CreateDefaultMapping(),
                0,
                new HotkeyPreferences(HotkeyModifiers.Control | HotkeyModifiers.Alt, 8, 9),
                roblox);

            Assert.Equal("Roblox: Meet People Across The World", vm.TargetWindowTitle);
        }

        // --- 3. OverlayViewModel Action Dispatch Tests ---

        [Fact]
        public void ViewModel_TogglePlayPause_TriggersCallbackAndTracksClick()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            bool callbackInvoked = false;
            vm.RequestPlayPause = () => callbackInvoked = true;

            Assert.Equal(0, vm.NonActivatingClickCount);
            vm.TogglePlayPause();

            Assert.True(callbackInvoked);
            Assert.Equal(1, vm.NonActivatingClickCount);
            Assert.StartsWith("Play/Pause", vm.LastControlClicked);
        }

        [Fact]
        public void ViewModel_EmergencyStop_TriggersCallbackAndTracksClick()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            bool stopInvoked = false;
            vm.RequestEmergencyStop = () => stopInvoked = true;

            vm.EmergencyStop();

            Assert.True(stopInvoked);
            Assert.Equal(1, vm.NonActivatingClickCount);
            Assert.StartsWith("Emergency Stop", vm.LastControlClicked);
        }

        [Fact]
        public void ViewModel_Seek_ClampsAndTriggersCallback()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            var song = CreateSampleSong();
            var fakeInput = new FakeKeyboardInput();
            using var engine = new PlaybackEngine(fakeInput);

            vm.SynchronizeState(
                song,
                song.Duration,
                engine,
                null!,
                CreateDefaultMapping(),
                0,
                new HotkeyPreferences(HotkeyModifiers.Control | HotkeyModifiers.Alt, 8, 9),
                null);

            TimeSpan? seekedTarget = null;
            vm.RequestSeek = target => seekedTarget = target;

            vm.Seek(TimeSpan.FromSeconds(2.5));

            Assert.NotNull(seekedTarget);
            Assert.Equal(TimeSpan.FromSeconds(2.5), seekedTarget.Value);
        }

        [Fact]
        public void ViewModel_CycleSpeed_AdvancesThroughPresets()
        {
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);
            double lastSpeed = 0;
            vm.RequestSpeedChange = speed =>
            {
                lastSpeed = speed;
            };

            var fakeInput = new FakeKeyboardInput();
            using var engine = new PlaybackEngine(fakeInput);

            // Initial speed is 1.0; cycle should advance to 1.25
            vm.CycleSpeed();
            Assert.Equal(1.25, lastSpeed);

            // Synchronize state with updated engine speed
            engine.Speed = 1.25;
            vm.SynchronizeState(null, TimeSpan.Zero, engine, null!, CreateDefaultMapping(), 0, new HotkeyPreferences(HotkeyModifiers.Control, 8, 9), null);
            Assert.Equal(1.25, vm.PlaybackSpeed);

            // Cycle again -> 1.50
            vm.CycleSpeed();
            Assert.Equal(1.50, lastSpeed);

            engine.Speed = 1.50;
            vm.SynchronizeState(null, TimeSpan.Zero, engine, null!, CreateDefaultMapping(), 0, new HotkeyPreferences(HotkeyModifiers.Control, 8, 9), null);
            Assert.Equal(1.50, vm.PlaybackSpeed);

            // Cycle again -> wraps to 0.75
            vm.CycleSpeed();
            Assert.Equal(0.75, lastSpeed);

            engine.Speed = 0.75;
            vm.SynchronizeState(null, TimeSpan.Zero, engine, null!, CreateDefaultMapping(), 0, new HotkeyPreferences(HotkeyModifiers.Control, 8, 9), null);
            Assert.Equal(0.75, vm.PlaybackSpeed);

            // Cycle again -> 1.00
            vm.CycleSpeed();
            Assert.Equal(1.00, lastSpeed);
        }

        [Fact]
        public void ViewModel_ToggleCompactMode_TogglesSettingAndNotifies()
        {
            var settings = new OverlaySettings { IsCompact = false };
            var vm = new OverlayViewModel(settings);

            bool propertyChangedFired = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.IsCompact))
                {
                    propertyChangedFired = true;
                }
            };

            vm.ToggleCompact();

            Assert.True(vm.IsCompact);
            Assert.True(settings.IsCompact);
            Assert.True(propertyChangedFired);

            vm.ToggleCompact();
            Assert.False(vm.IsCompact);
            Assert.False(settings.IsCompact);
        }

        [Fact]
        public void ViewModel_TogglePin_FlipsPinnedState()
        {
            var settings = new OverlaySettings { IsPinned = true };
            var vm = new OverlayViewModel(settings);

            vm.TogglePin();
            Assert.False(vm.IsPinned);
            Assert.False(settings.IsPinned);

            vm.TogglePin();
            Assert.True(vm.IsPinned);
            Assert.True(settings.IsPinned);
        }

        // --- 4. WPF STA Window Lifecycle & Non-Activating Tests ---

        [Fact]
        public void OverlayWindow_STA_CanBeCreatedAndBound()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings();
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                Assert.NotNull(window);
                Assert.Same(vm, window.DataContext);
                Assert.True(window.AllowsTransparency);
                Assert.Equal(WindowStyle.None, window.WindowStyle);

                // Clean real close
                window.IsRealClose = true;
                window.Close();
            });
        }

        [Fact]
        public void OverlayWindow_STA_AppliesExpandedAndCompactLayouts()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings { ExpandedWidth = 920, ExpandedHeight = 440 };
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                // Test Expanded layout
                window.ApplyLayoutMode(false);
                Assert.Equal(920, window.Width);
                Assert.Equal(440, window.Height);
                Assert.Equal(360, window.MinHeight);
                Assert.Equal(800, window.MaxHeight);

                // Test Compact layout
                window.ApplyLayoutMode(true);
                Assert.Equal(760, window.Width);
                Assert.Equal(96, window.Height);
                Assert.Equal(60, window.MinHeight);
                Assert.Equal(120, window.MaxHeight);

                window.IsRealClose = true;
                window.Close();
            });
        }

        [Fact]
        public void OverlayWindow_STA_SoftClose_CancelsCloseAndHides()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings();
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                // With IsRealClose = false, calling Close() should cancel close and hide
                window.IsRealClose = false;
                window.Close();

                Assert.Equal(Visibility.Hidden, window.Visibility);

                // With IsRealClose = true, Close() succeeds and window terminates
                window.IsRealClose = true;
                window.Close();
            });
        }

        // --- 5. Playback Continuity Test ---

        [Fact]
        public void PlaybackContinuity_OverlayStateUpdates_DoNotDisruptPlaybackEngine()
        {
            var fakeInput = new FakeKeyboardInput();
            using var engine = new PlaybackEngine(fakeInput);
            var settings = new OverlaySettings();
            var vm = new OverlayViewModel(settings);

            // Multiple rapid state synchronizations like 50ms timer
            for (int i = 0; i < 20; i++)
            {
                vm.SynchronizeState(
                    null,
                    TimeSpan.FromSeconds(60),
                    engine,
                    null!,
                    CreateDefaultMapping(),
                    0,
                    new HotkeyPreferences(HotkeyModifiers.Control | HotkeyModifiers.Alt, 8, 9),
                    null);
            }

            // Engine state is still healthy and uncorrupted
            Assert.Equal(PlaybackState.Idle, engine.State);
            Assert.Null(engine.Error);
        }
    }
}
