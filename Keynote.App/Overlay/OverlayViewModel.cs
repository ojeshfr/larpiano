using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Keynote.Core;
using Keynote.Infrastructure;
using Keynote.Input;
using Keynote.Playback;
using Keynote.Online;

namespace Keynote.App.Overlay
{
    public class OverlayViewModel : INotifyPropertyChanged
    {
        private readonly OverlaySettings _settings;

        // Real song & playback state
        private MidiSong? _song;
        private TimeSpan _songDuration = TimeSpan.Zero;
        private PlaybackState _playbackState = PlaybackState.Idle;
        private TimeSpan _currentPosition = TimeSpan.Zero;
        private double _playbackSpeed = 1.0;
        private string? _playbackError;

        // Target window & mapping
        private string _targetWindowTitle = "No Roblox window selected";
        private bool _isTargetForeground = false;
        private string _mappingName = "Visual Pianos";
        private bool _isMappingVerified = true;
        private int _missingPitchCount = 0;
        private KeyboardMapping? _mapping;
        private int _octave = 0;

        // Hotkeys
        private string _shortcutPlayPause = "Ctrl + Alt + F8";
        private string _shortcutStop = "Ctrl + Alt + F9";

        // Diagnostics
        private PlaybackDiagnostics _diagnostics = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

        // Popovers
        private bool _isSpeedDropdownOpen = false;
        private bool _isMappingPopoverOpen = false;
        private bool _isDiagnosticsPopoverOpen = false;
        private bool _isSettingsPopoverOpen = false;

        // Non-activation tracking
        private string _lastControlClicked = "None";
        private int _nonActivatingClickCount = 0;

        // Action hooks to main application
        public Action? RequestPlayPause { get; set; }
        public Action? RequestEmergencyStop { get; set; }
        public Action<TimeSpan>? RequestSeek { get; set; }
        public Action<double>? RequestSpeedChange { get; set; }
        public Action? RequestOpenMappingTab { get; set; }
        public Action? RequestOpenDiagnosticsTab { get; set; }
        public Action<LibraryEntry>? RequestSelectSong { get; set; }
        public Action? RequestBrowseMidi { get; set; }
        public Action<int>? RequestOpenSection { get; set; }
        public void BrowseMidi() => RequestBrowseMidi?.Invoke();
        public void OpenSection(int index) { if (index is >= 0 and <= 8) RequestOpenSection?.Invoke(index); }
        private IReadOnlyList<LibraryEntry> _librarySongs = Array.Empty<LibraryEntry>();
        private LibraryEntry? _selectedLibrarySong;
        public IReadOnlyList<LibraryEntry> LibrarySongs => _librarySongs;
        public LibraryEntry? SelectedLibrarySong {
            get => _selectedLibrarySong;
            set { if (SetField(ref _selectedLibrarySong, value) && value != null) RequestSelectSong?.Invoke(value); }
        }
        public void SynchronizeLibrary(IReadOnlyList<LibraryEntry> songs, LibraryEntry? selected) {
            if (!ReferenceEquals(_librarySongs, songs)) { _librarySongs = songs; OnPropertyChanged(nameof(LibrarySongs)); }
            SetField(ref _selectedLibrarySong, selected, nameof(SelectedLibrarySong));
        }

        public OverlayViewModel(OverlaySettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // --- Song & Metadata ---

        public MidiSong? Song
        {
            get => _song;
            private set
            {
                if (SetField(ref _song, value))
                {
                    OnPropertyChanged(nameof(SongTitle));
                    OnPropertyChanged(nameof(SongSubtitle));
                    OnPropertyChanged(nameof(HasSong));
                }
            }
        }

        public bool HasSong => _song != null;

        public string SongTitle => _song?.Name ?? "No song loaded";

        public string SongSubtitle
        {
            get
            {
                if (_song == null) return "Choose a song or import a MIDI file in Larpiano";
                return $"{_song.Tracks.Count} tracks • {_song.Notes.Count()} notes • {FormatTime(_songDuration)}";
            }
        }

        // --- Playback State ---

        public PlaybackState PlaybackState
        {
            get => _playbackState;
            private set
            {
                if (SetField(ref _playbackState, value))
                {
                    OnPropertyChanged(nameof(PlaybackStateText));
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        public string PlaybackStateText
        {
            get
            {
                if (!string.IsNullOrEmpty(_playbackError)) return _playbackError;
                return _playbackState switch
                {
                    PlaybackState.Playing => "Playing",
                    PlaybackState.Paused => "Paused",
                    PlaybackState.Stopped => "Stopped",
                    PlaybackState.Ready => "Ready",
                    PlaybackState.Faulted => "Faulted",
                    _ => "Idle"
                };
            }
        }

        public bool IsPlaying => _playbackState == PlaybackState.Playing;

        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            private set
            {
                if (SetField(ref _currentPosition, value))
                {
                    OnPropertyChanged(nameof(TimeString));
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public TimeSpan TotalDuration
        {
            get => _songDuration;
            private set
            {
                if (SetField(ref _songDuration, value))
                {
                    OnPropertyChanged(nameof(TimeString));
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(SongSubtitle));
                }
            }
        }

        public double Progress
        {
            get
            {
                if (_songDuration <= TimeSpan.Zero) return 0.0;
                return Math.Clamp(_currentPosition.TotalSeconds / _songDuration.TotalSeconds, 0.0, 1.0);
            }
            set
            {
                if (_songDuration > TimeSpan.Zero)
                {
                    var target = TimeSpan.FromSeconds(Math.Clamp(value, 0.0, 1.0) * _songDuration.TotalSeconds);
                    Seek(target);
                }
            }
        }

        public string TimeString => $"{FormatTime(_currentPosition)} / {FormatTime(_songDuration)}";

        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            private set
            {
                if (SetField(ref _playbackSpeed, value))
                {
                    OnPropertyChanged(nameof(SpeedText));
                    OnPropertyChanged(nameof(SpeedControl));
                }
            }
        }

        public string SpeedText => $"{_playbackSpeed:0.00}×";
        public double SpeedControl {
            get => PlaybackSpeed;
            set { if (double.IsFinite(value) && value >= 0.5 && value <= 2 && Math.Abs(value - PlaybackSpeed) > 0.001) RequestSpeedChange?.Invoke(value); }
        }

        // --- Target & Mapping ---

        public string TargetWindowTitle => _targetWindowTitle;

        public bool IsTargetForeground => _isTargetForeground;

        public string TargetForegroundStatus => _isTargetForeground ? "• Foreground" : "• In background";

        public string MappingName => _mappingName;

        public bool IsMappingVerified => _isMappingVerified;

        public KeyboardMapping? Mapping => _mapping;

        public int Octave => _octave;

        public string MappingStatusText
        {
            get
            {
                string status = _isMappingVerified ? "verified" : "unverified";
                if (_missingPitchCount > 0)
                {
                    return $"Mapping: {_mappingName} · {status} ({_missingPitchCount} unmapped)";
                }
                return $"Mapping: {_mappingName} · {status}";
            }
        }

        // --- Shortcuts & Diagnostics ---

        public string ShortcutPlayPause => _shortcutPlayPause;
        public string ShortcutStop => _shortcutStop;

        public PlaybackDiagnostics Diagnostics => _diagnostics;

        public string DiagnosticsSummary =>
            $"Scheduled: {_diagnostics.Scheduled} | Dispatched: {_diagnostics.Dispatched} | Failed: {_diagnostics.Failed} | Late: {_diagnostics.Late}\n" +
            $"Avg / p95 / Max Late: {_diagnostics.AverageLateMs:F1} / {_diagnostics.P95LateMs:F0} / {_diagnostics.MaxLateMs:F1} ms\n" +
            $"Focus losses: {_diagnostics.FocusLosses} | Cleanup failures: {_diagnostics.CleanupFailures}";

        public string LastControlClicked => _lastControlClicked;
        public int NonActivatingClickCount => _nonActivatingClickCount;

        // --- Settings & Window State ---

        public bool IsCompact
        {
            get => _settings.IsCompact;
            set
            {
                if (_settings.IsCompact != value)
                {
                    _settings.IsCompact = value;
                    OnPropertyChanged();
                    _settings.Save();
                }
            }
        }

        public bool IsPinned
        {
            get => _settings.IsPinned;
            set
            {
                if (_settings.IsPinned != value)
                {
                    _settings.IsPinned = value;
                    OnPropertyChanged();
                    _settings.Save();
                }
            }
        }

        public bool WarmupEnabled {
            get => _settings.WarmupEnabled;
            set { if (_settings.WarmupEnabled != value) { _settings.WarmupEnabled = value; OnPropertyChanged(); _settings.Save(); } }
        }

        public double GlassOpacity
        {
            get => _settings.GlassOpacity;
            set
            {
                double clamped = Math.Clamp(value, 0.70, 1.0);
                if (Math.Abs(_settings.GlassOpacity - clamped) > 0.001)
                {
                    _settings.GlassOpacity = clamped;
                    OnPropertyChanged();
                    _settings.Save();
                }
            }
        }

        // --- Popover Visibility ---

        public bool IsSpeedDropdownOpen
        {
            get => _isSpeedDropdownOpen;
            set => SetField(ref _isSpeedDropdownOpen, value);
        }

        public bool IsMappingPopoverOpen
        {
            get => _isMappingPopoverOpen;
            set
            {
                if (SetField(ref _isMappingPopoverOpen, value) && value)
                {
                    IsDiagnosticsPopoverOpen = false;
                    IsSettingsPopoverOpen = false;
                    IsSpeedDropdownOpen = false;
                }
            }
        }

        public bool IsDiagnosticsPopoverOpen
        {
            get => _isDiagnosticsPopoverOpen;
            set
            {
                if (SetField(ref _isDiagnosticsPopoverOpen, value) && value)
                {
                    IsMappingPopoverOpen = false;
                    IsSettingsPopoverOpen = false;
                    IsSpeedDropdownOpen = false;
                }
            }
        }

        public bool IsSettingsPopoverOpen
        {
            get => _isSettingsPopoverOpen;
            set
            {
                if (SetField(ref _isSettingsPopoverOpen, value) && value)
                {
                    IsMappingPopoverOpen = false;
                    IsDiagnosticsPopoverOpen = false;
                    IsSpeedDropdownOpen = false;
                }
            }
        }

        // --- State Synchronization from Main Application ---

        public void SynchronizeState(
            MidiSong? loadedSong,
            TimeSpan duration,
            PlaybackEngine engine,
            WindowsKeyboardInput input,
            KeyboardMapping profile,
            int octaveShift,
            HotkeyPreferences hotkeys,
            RobloxWindow? targetWindow)
        {
            Song = loadedSong;
            TotalDuration = duration;

            if (engine != null)
            {
                SetField(ref _playbackError, engine.Error, nameof(PlaybackStateText));
                PlaybackState = engine.State;
                CurrentPosition = engine.Position > duration && duration > TimeSpan.Zero ? duration : engine.Position;
                PlaybackSpeed = engine.Speed;
                _diagnostics = engine.Diagnostics;
                OnPropertyChanged(nameof(Diagnostics));
                OnPropertyChanged(nameof(DiagnosticsSummary));
            }

            if (input != null)
            {
                if (SetField(ref _isTargetForeground, input.IsTargetForeground, nameof(IsTargetForeground)))
                {
                    OnPropertyChanged(nameof(TargetForegroundStatus));
                }
            }

            if (targetWindow != null)
            {
                string title = targetWindow.Title ?? "Roblox";
                string displayTitle = title.StartsWith("Roblox", StringComparison.OrdinalIgnoreCase) ? title.Trim() : $"Roblox: {title}";
                SetField(ref _targetWindowTitle, displayTitle, nameof(TargetWindowTitle));
            }
            else
            {
                SetField(ref _targetWindowTitle, "No Roblox window selected", nameof(TargetWindowTitle));
            }

            _mapping = profile;
            _octave = octaveShift;
            OnPropertyChanged(nameof(Mapping));
            OnPropertyChanged(nameof(Octave));

            if (profile != null)
            {
                _mappingName = profile.Name;
                _isMappingVerified = profile.Verified;
                _missingPitchCount = loadedSong == null ? 0 : profile.Missing(loadedSong, octaveShift).Length;
                OnPropertyChanged(nameof(MappingName));
                OnPropertyChanged(nameof(IsMappingVerified));
                OnPropertyChanged(nameof(MappingStatusText));
            }

            if (hotkeys != null)
            {
                var toggle = new Hotkey(hotkeys.Modifiers, (uint)(0x6F + hotkeys.ToggleFunction));
                var stop = new Hotkey(hotkeys.Modifiers, (uint)(0x6F + hotkeys.StopFunction));
                _shortcutPlayPause = toggle.ToString();
                _shortcutStop = stop.ToString();
                OnPropertyChanged(nameof(ShortcutPlayPause));
                OnPropertyChanged(nameof(ShortcutStop));
            }
        }

        // --- User Actions ---

        public void RecordClick(string control)
        {
            _lastControlClicked = $"{control} ({DateTime.Now:HH:mm:ss})";
            _nonActivatingClickCount++;
            OnPropertyChanged(nameof(LastControlClicked));
            OnPropertyChanged(nameof(NonActivatingClickCount));
        }

        public void TogglePlayPause()
        {
            RecordClick("Play/Pause");
            RequestPlayPause?.Invoke();
        }

        public void EmergencyStop()
        {
            RecordClick("Emergency Stop");
            RequestEmergencyStop?.Invoke();
        }

        public void Seek(TimeSpan target)
        {
            RecordClick($"Seek ({FormatTime(target)})");
            RequestSeek?.Invoke(target);
        }

        public void SeekForward(TimeSpan delta)
        {
            if (_songDuration > TimeSpan.Zero)
            {
                var target = _currentPosition + delta;
                if (target > _songDuration) target = _songDuration;
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                Seek(target);
            }
        }

        public void SetSpeed(double speed)
        {
            RecordClick($"Speed {speed:0.00}x");
            IsSpeedDropdownOpen = false;
            RequestSpeedChange?.Invoke(speed);
        }

        public void CycleSpeed()
        {
            double nextSpeed = PlaybackSpeed switch
            {
                < 0.9 => 1.00,
                < 1.1 => 1.25,
                < 1.4 => 1.50,
                _ => 0.75
            };
            SetSpeed(nextSpeed);
        }

        public void ToggleCompact()
        {
            RecordClick("Toggle Compact");
            IsCompact = !IsCompact;
            IsMappingPopoverOpen = false;
            IsDiagnosticsPopoverOpen = false;
            IsSettingsPopoverOpen = false;
            IsSpeedDropdownOpen = false;
        }

        public void TogglePin()
        {
            RecordClick("Toggle Pin");
            IsPinned = !IsPinned;
        }

        public void ToggleSpeedDropdown()
        {
            RecordClick("Speed Menu");
            IsSpeedDropdownOpen = !IsSpeedDropdownOpen;
        }

        public void ToggleMappingPopover()
        {
            RecordClick("Mapping Details");
            IsMappingPopoverOpen = !IsMappingPopoverOpen;
        }

        public void ToggleDiagnosticsPopover()
        {
            RecordClick("Diagnostics");
            IsDiagnosticsPopoverOpen = !IsDiagnosticsPopoverOpen;
        }

        public void ToggleSettingsPopover()
        {
            RecordClick("Overlay Settings");
            IsSettingsPopoverOpen = !IsSettingsPopoverOpen;
        }

        public void OpenMainWindowMapping()
        {
            RecordClick("Open Mapping Tab");
            IsMappingPopoverOpen = false;
            RequestOpenMappingTab?.Invoke();
        }

        public void OpenMainWindowDiagnostics()
        {
            RecordClick("Open Diagnostics Tab");
            IsDiagnosticsPopoverOpen = false;
            RequestOpenDiagnosticsTab?.Invoke();
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
            }
            return $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
        }
    }
}
