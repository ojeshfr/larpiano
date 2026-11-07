using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Keynote.App.Overlay;
using Xunit;

namespace Keynote.Overlay.Tests
{
    [Collection("STA")]
    public class LiveWindowsInteractionTests
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;

        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

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

        [Fact]
        public void OverlayWindow_HasExpectedWin32ExtendedStyles()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings();
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                // Show without activating
                window.Show();

                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;
                Assert.NotEqual(IntPtr.Zero, hwnd);

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

                // Verify WS_EX_NOACTIVATE is set
                Assert.True((exStyle & WS_EX_NOACTIVATE) == WS_EX_NOACTIVATE,
                    $"Expected WS_EX_NOACTIVATE (0x08000000) to be set, but exStyle was 0x{exStyle:X8}");

                // Verify WS_EX_TOOLWINDOW is set (hides from Alt+Tab)
                Assert.True((exStyle & WS_EX_TOOLWINDOW) == WS_EX_TOOLWINDOW,
                    $"Expected WS_EX_TOOLWINDOW (0x00000080) to be set, but exStyle was 0x{exStyle:X8}");

                // Verify WS_EX_TOPMOST is set when pinned
                Assert.True((exStyle & WS_EX_TOPMOST) == WS_EX_TOPMOST,
                    $"Expected WS_EX_TOPMOST (0x00000008) to be set, but exStyle was 0x{exStyle:X8}");

                window.IsRealClose = true;
                window.Close();
            });
        }

        [Fact]
        public void OverlayWindow_MouseActivateMessage_ReturnsMaNoActivate()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings();
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                window.Show();

                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;

                // Send WM_MOUSEACTIVATE to the overlay window
                IntPtr result = SendMessage(hwnd, WM_MOUSEACTIVATE, hwnd, IntPtr.Zero);

                // MA_NOACTIVATE = 3
                Assert.Equal(new IntPtr(MA_NOACTIVATE), result);

                window.IsRealClose = true;
                window.Close();
            });
        }

        [Fact]
        public void OverlayWindow_ShowAndClick_PreservesForegroundWindow()
        {
            RunInSta(() =>
            {
                // 1. Create a dummy foreground target window (representing Roblox)
                var targetWindow = new Window
                {
                    Title = "Roblox: Meet People Across The World",
                    Width = 400,
                    Height = 300,
                    WindowStyle = WindowStyle.SingleBorderWindow
                };
                targetWindow.Show();
                var targetHwnd = new WindowInteropHelper(targetWindow).Handle;

                SetForegroundWindow(targetHwnd);
                Thread.Sleep(50);

                IntPtr initialForeground = GetForegroundWindow();

                // 2. Create and show OverlayWindow
                var settings = new OverlaySettings();
                var vm = new OverlayViewModel(settings);
                var overlayWindow = new OverlayWindow(vm, settings);

                overlayWindow.Show();
                var overlayHwnd = new WindowInteropHelper(overlayWindow).Handle;

                Thread.Sleep(50);
                IntPtr foregroundAfterShow = GetForegroundWindow();

                // OverlayWindow MUST NOT have taken foreground away from the target window
                Assert.NotEqual(overlayHwnd, foregroundAfterShow);

                // 3. Send mouse clicks to overlay window
                SendMessage(overlayHwnd, WM_LBUTTONDOWN, IntPtr.Zero, IntPtr.Zero);
                SendMessage(overlayHwnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);

                Thread.Sleep(50);
                IntPtr foregroundAfterClick = GetForegroundWindow();

                // Even after mouse click, overlay window MUST NOT become the foreground window
                Assert.NotEqual(overlayHwnd, foregroundAfterClick);

                // Cleanup
                overlayWindow.IsRealClose = true;
                overlayWindow.Close();
                targetWindow.Close();
            });
        }

        [Fact]
        public void OverlayWindow_UnpinAndPin_TogglesTopmostExtendedStyle()
        {
            RunInSta(() =>
            {
                var settings = new OverlaySettings { IsPinned = true };
                var vm = new OverlayViewModel(settings);
                var window = new OverlayWindow(vm, settings);

                window.Show();
                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;

                // Initial: Pinned -> Topmost
                int style1 = GetWindowLong(hwnd, GWL_EXSTYLE);
                Assert.True((style1 & WS_EX_TOPMOST) == WS_EX_TOPMOST);

                // Unpin
                vm.TogglePin();
                int style2 = GetWindowLong(hwnd, GWL_EXSTYLE);
                Assert.False((style2 & WS_EX_TOPMOST) == WS_EX_TOPMOST);

                // Re-pin
                vm.TogglePin();
                int style3 = GetWindowLong(hwnd, GWL_EXSTYLE);
                Assert.True((style3 & WS_EX_TOPMOST) == WS_EX_TOPMOST);

                window.IsRealClose = true;
                window.Close();
            });
        }
    }
}
