using System;
using System.Windows;
using Keynote.Core;
using Keynote.Infrastructure;
using Keynote.Input;
using Keynote.Playback;

namespace Keynote.App.Overlay
{
    public sealed class OverlayWindowService : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly OverlaySettings _settings;
        private readonly OverlayViewModel _viewModel;
        private OverlayWindow? _overlayWindow;
        private bool _disposed;

        public OverlayViewModel ViewModel => _viewModel;
        public OverlaySettings Settings => _settings;

        public bool IsVisible => _overlayWindow != null && _overlayWindow.IsVisible;

        public OverlayWindowService(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _settings = OverlaySettings.Load();
            _viewModel = new OverlayViewModel(_settings)
            {
                RequestPlayPause = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.PlayPauseFromOverlay()),
                RequestEmergencyStop = () => _mainWindow.Dispatcher.Invoke(async () => await _mainWindow.EmergencyStopFromOverlay()),
                RequestSeek = target => _mainWindow.Dispatcher.Invoke(() => _mainWindow.SeekFromOverlay(target)),
                RequestSpeedChange = speed => _mainWindow.Dispatcher.Invoke(() => _mainWindow.SetSpeedFromOverlay(speed)),
                RequestOpenMappingTab = () => _mainWindow.Dispatcher.Invoke(() => { Hide(); _mainWindow.OpenSectionFromOverlay(4); }),
                RequestOpenDiagnosticsTab = () => _mainWindow.Dispatcher.Invoke(() => { Hide(); _mainWindow.OpenSectionFromOverlay(5); }),
                RequestSelectSong = entry => _mainWindow.Dispatcher.Invoke(() => _mainWindow.SelectSongFromOverlay(entry)),
                RequestBrowseMidi = () => _mainWindow.Dispatcher.Invoke(() => {
                    Hide();
                    try { _mainWindow.BrowseFromOverlay(); }
                    finally { Show(); }
                }),
                RequestOpenSection = index => _mainWindow.Dispatcher.Invoke(() => { Hide(); _mainWindow.OpenSectionFromOverlay(index); }),
                RequestRefreshWindows = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.RefreshWindowsFromOverlay()),
                RequestSelectPlaylist = p => _mainWindow.Dispatcher.Invoke(() => _mainWindow.SelectPlaylistFromOverlay(p)),
                RequestPlayNext = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.PlayNextFromOverlay()),
                RequestPlayPrevious = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.PlayPreviousFromOverlay()),
                RequestToggleShuffle = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.ToggleShuffleFromOverlay()),
                RequestCycleRepeat = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.CycleRepeatFromOverlay()),
                RequestPlayQueueItem = item => _mainWindow.Dispatcher.Invoke(() => _mainWindow.PlayQueueItemFromOverlay(item)),
                RequestRemoveFromQueue = id => _mainWindow.Dispatcher.Invoke(() => _mainWindow.RemoveFromQueueFromOverlay(id)),
                RequestClearQueue = () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.ClearQueueFromOverlay())
            };
        }

        private void EnsureWindowCreated()
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow(_viewModel, _settings);
            }
        }

        public void Show()
        {
            EnsureWindowCreated();
            if (_overlayWindow != null)
            {
                if (!_overlayWindow.IsVisible)
                {
                    _overlayWindow.Show();
                }
                else
                {
                    _overlayWindow.Activate();
                }
            }
        }

        public void Hide()
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.Hide();
            }
        }

        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void UpdateState(
            MidiSong? loadedSong,
            TimeSpan duration,
            PlaybackEngine engine,
            WindowsKeyboardInput input,
            KeyboardMapping profile,
            int octaveShift,
            HotkeyPreferences hotkeys,
            RobloxWindow? targetWindow)
        {
            _viewModel.SynchronizeState(loadedSong, duration, engine, input, profile, octaveShift, hotkeys, targetWindow);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_overlayWindow != null)
            {
                _overlayWindow.IsRealClose = true;
                _overlayWindow.Close();
                _overlayWindow = null;
            }
        }
    }
}
