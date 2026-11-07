using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Keynote.App.Overlay.Views
{
    public partial class CompactOverlayView : UserControl
    {
        public CompactOverlayView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? VM => DataContext as OverlayViewModel;

        private void OnBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var window = Window.GetWindow(this) as OverlayWindow;
                window?.StartNonActivatingDrag(e);
            }
        }

        private void OnPlayPauseClick(object sender, RoutedEventArgs e) => VM?.TogglePlayPause();
        private void OnBrowseMidiClick(object sender, RoutedEventArgs e) => VM?.BrowseMidi();
        private void OnStopClick(object sender, RoutedEventArgs e) => VM?.EmergencyStop();
        private void OnSpeedClick(object sender, RoutedEventArgs e) => VM?.CycleSpeed();
        private void OnExpandClick(object sender, RoutedEventArgs e) => VM?.ToggleCompact();

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this) as OverlayWindow;
            window?.Hide();
        }
    }
}
