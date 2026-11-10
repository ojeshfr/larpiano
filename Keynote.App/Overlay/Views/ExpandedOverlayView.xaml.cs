using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Keynote.App.Overlay.Views
{
    public partial class ExpandedOverlayView : UserControl
    {
        public ExpandedOverlayView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            PianoRoll.SeekRequested += target => VM?.Seek(target);
        }

        private OverlayViewModel? VM => DataContext as OverlayViewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is OverlayViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (e.NewValue is OverlayViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdatePianoRoll(newVm);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is OverlayViewModel vm)
            {
                if (e.PropertyName == nameof(OverlayViewModel.Song))
                {
                    PianoRoll.Song = vm.Song;
                }
                else if (e.PropertyName == nameof(OverlayViewModel.CurrentPosition))
                {
                    PianoRoll.Position = vm.CurrentPosition;
                }
                else if (e.PropertyName == nameof(OverlayViewModel.Mapping) || e.PropertyName == nameof(OverlayViewModel.Octave))
                {
                    PianoRoll.Mapping = vm.Mapping;
                    PianoRoll.Octave = vm.Octave;
                    PianoRoll.InvalidateVisual();
                }
            }
        }

        private void UpdatePianoRoll(OverlayViewModel vm)
        {
            PianoRoll.Song = vm.Song;
            PianoRoll.Position = vm.CurrentPosition;
            PianoRoll.Mapping = vm.Mapping;
            PianoRoll.Octave = vm.Octave;
        }

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var window = Window.GetWindow(this) as OverlayWindow;
                window?.StartNonActivatingDrag(e);
            }
        }

        private void OnPlayPauseClick(object sender, RoutedEventArgs e) => VM?.TogglePlayPause();
        private void OnTargetCardClick(object sender, MouseButtonEventArgs e) => VM?.RefreshTargetWindow();
        private void OnBrowseMidiClick(object sender, RoutedEventArgs e) => VM?.BrowseMidi();
        private void OnAppMenuClick(object sender, RoutedEventArgs e) { if (sender is Button button && button.ContextMenu is { } menu) { menu.PlacementTarget = button; menu.IsOpen = true; } }
        private void OnOpenSectionClick(object sender, RoutedEventArgs e) { if (sender is MenuItem item && int.TryParse(item.Tag?.ToString(), out int index)) VM?.OpenSection(index); }
        private void OnStopClick(object sender, RoutedEventArgs e) => VM?.EmergencyStop();
        private void OnPreviousClick(object sender, RoutedEventArgs e)
        {
            if (VM?.IsPlayingPlaylist == true)
                VM.PlayPreviousTrack();
            else
                VM?.Seek(TimeSpan.Zero);
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (VM?.IsPlayingPlaylist == true)
                VM.PlayNextTrack();
            else
                VM?.SeekForward(TimeSpan.FromSeconds(10));
        }

        private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OverlayPlaylistSelector.SelectedItem is Keynote.Core.Playlist p)
            {
                VM?.SelectPlaylist(p);
            }
            else if (OverlayPlaylistSelector.SelectedItem == null)
            {
                VM?.SelectPlaylist(null);
            }
        }

        private void OnToggleShuffleClick(object sender, RoutedEventArgs e) => VM?.ToggleShuffle();
        private void OnCycleRepeatClick(object sender, RoutedEventArgs e) => VM?.CycleRepeat();
        private void OnCloseQueueDrawerClick(object sender, RoutedEventArgs e) => VM?.ToggleQueueDrawer();

        private void OnPlayQueueItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Keynote.Core.PlaylistItem item)
            {
                VM?.PlayQueueItem(item);
            }
        }

        private void OnRemoveQueueItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Keynote.Core.PlaylistItem item)
            {
                VM?.RemoveFromQueue(item.Id);
            }
        }

        private void OnClearQueueClick(object sender, RoutedEventArgs e) => VM?.ClearQueue();
        private void OnSpeedDropdownClick(object sender, RoutedEventArgs e) => VM?.ToggleSpeedDropdown();

        private void OnSelectSpeed(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && double.TryParse(tagStr, out double speed))
            {
                VM?.SetSpeed(speed);
            }
        }

        private void OnCompactClick(object sender, RoutedEventArgs e) => VM?.ToggleCompact();
        private void OnSettingsClick(object sender, RoutedEventArgs e) => VM?.ToggleSettingsPopover();
        private void OnMappingClick(object sender, RoutedEventArgs e) => VM?.ToggleMappingPopover();
        private void OnDiagnosticsClick(object sender, RoutedEventArgs e) => VM?.ToggleDiagnosticsPopover();
        private void OnOpenMappingTabClick(object sender, RoutedEventArgs e) => VM?.OpenMainWindowMapping();
        private void OnOpenDiagnosticsTabClick(object sender, RoutedEventArgs e) => VM?.OpenMainWindowDiagnostics();

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this) as OverlayWindow;
            window?.Hide();
        }
    }
}
