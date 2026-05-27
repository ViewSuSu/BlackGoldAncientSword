using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Prism.Modularity;
using Prism.Regions;
using BlackGoldAncientSword.Framework.Core.Bases;

namespace BlackGoldAncientSword.App.Shell
{
    public partial class MainWindow
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_DPICHANGED = 0x02E0;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        /// <summary>
        /// Invisible resize border thickness for custom hit-test via WndProc.
        /// </summary>
        private const int ResizeBorder = 4;

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        private IntPtr _hwnd;
        private bool _isExiting;

        static MainWindow()
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(200));
        }

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            StateChanged += OnWindowStateChanged;
            Closing += OnWindowClosing;
        }

        public void MinimizeToTray()
        {
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            // Not used for tray minimize - we use explicit Hide() instead
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExiting)
                return;

            try
            {
                var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<Framework.Services.Abstractions.ISettingsService>();
                switch (settings.Current.CloseBehavior)
                {
                    case "MinimizeToTaskbar":
                        WindowState = WindowState.Minimized;
                        e.Cancel = true;
                        return;
                    case "MinimizeToTray":
                        e.Cancel = true;
                        Hide();
                        return;
                }
            }
            catch { }

            _isExiting = true;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                // Use GetDpiForWindow to always use the current monitor DPI,
                // avoiding stale DPI after moving between monitors with different scaling.
                int dpi = GetDpiForWindow(_hwnd);
                double scale = dpi / 96.0;

                // Extract signed screen coordinates (can be negative on multi-monitor)
                int screenX = (short)((int)lParam & 0xFFFF);
                int screenY = (short)(((int)lParam >> 16) & 0xFFFF);

                // Convert physical screen coords to DIP client coords using correct DPI
                double ptX = (screenX / scale) - Left;
                double ptY = (screenY / scale) - Top;

                double w = ActualWidth;
                double h = ActualHeight;

                bool inTop = ptY < ResizeBorder;
                bool inBottom = ptY > h - ResizeBorder;
                bool inLeft = ptX < ResizeBorder;
                bool inRight = ptX > w - ResizeBorder;

                // Corners (checked first so they take priority over edges)
                if (inTop && inLeft)
                {
                    handled = true;
                    return (IntPtr)HTTOPLEFT;
                }
                if (inTop && inRight)
                {
                    handled = true;
                    return (IntPtr)HTTOPRIGHT;
                }
                if (inBottom && inLeft)
                {
                    handled = true;
                    return (IntPtr)HTBOTTOMLEFT;
                }
                if (inBottom && inRight)
                {
                    handled = true;
                    return (IntPtr)HTBOTTOMRIGHT;
                }

                // Edges
                if (inTop)
                {
                    handled = true;
                    return (IntPtr)HTTOP;
                }
                if (inBottom)
                {
                    handled = true;
                    return (IntPtr)HTBOTTOM;
                }
                if (inLeft)
                {
                    handled = true;
                    return (IntPtr)HTLEFT;
                }
                if (inRight)
                {
                    handled = true;
                    return (IntPtr)HTRIGHT;
                }

                // Title bar drag area (between resize zone and window buttons)
                if (ptY >= ResizeBorder && ptY <= 32 && ptX > 70 && ptX < w - 100)
                {
                    handled = true;
                    return (IntPtr)HTCAPTION;
                }
            }
            return IntPtr.Zero;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<Framework.Services.Abstractions.ISettingsService>();
                var moduleManager = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<IModuleManager>();
                var regionManager = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<IRegionManager>();

                // If remembered, perform saved action directly
                if (settings.Current.CloseBehaviorRemembered)
                {
                    switch (settings.Current.CloseBehavior)
                    {
                        case "MinimizeToTaskbar":
                            WindowState = WindowState.Minimized;
                            return;
                        case "MinimizeToTray":
                            Hide();
                            return;
                        case "ExitDirectly":
                            _isExiting = true;
                            Close();
                            return;
                    }
                }

                // Show close prompt overlay
                var moduleName = "ClosePromptModule";
                try { moduleManager.LoadModule(moduleName); } catch { }
                regionManager.RequestNavigate(
                    Framework.Core.Consts.GlobalConstant.ClosePromptRegion,
                    Framework.Core.Consts.PageNames.ClosePromptPage);
            }
            catch
            {
                // Fallback: just close if anything goes wrong
                Close();
            }
        }

        private void ToastItemBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border) return;

            var showOpacity = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var showSlide = new DoubleAnimation(-40, 0, new Duration(TimeSpan.FromMilliseconds(300)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTargetProperty(showOpacity, new PropertyPath("Opacity"));
            Storyboard.SetTargetProperty(showSlide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            Storyboard.SetTarget(showOpacity, border);
            Storyboard.SetTarget(showSlide, border);

            var sb = new Storyboard();
            sb.Children.Add(showOpacity);
            sb.Children.Add(showSlide);
            sb.Begin();

            var item = border.DataContext as ToastItem;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                HideToast(border, item);
            };
            timer.Start();
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayMenu_Show_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            Close();
        }


        private void HideToast(Border border, ToastItem? item)
        {
            var hideOpacity = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(400)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTargetProperty(hideOpacity, new PropertyPath("Opacity"));
            Storyboard.SetTarget(hideOpacity, border);
            var sb = new Storyboard();
            sb.Children.Add(hideOpacity);
            sb.Completed += (_, _) =>
            {
                if (item != null && DataContext is MainWindowViewModel vm)
                    vm.ToastItems.Remove(item);
            };

            sb.Begin();
        }
    }
}
