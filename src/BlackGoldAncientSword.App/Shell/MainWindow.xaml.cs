using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Prism.Modularity;
using Prism.Regions;
using BlackGoldAncientSword.Framework.Core.Bases;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.App.Shell
{
    public partial class MainWindow
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
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
        private bool IsAnyOverlayActive()
        {
            return AnnouncementOverlay.Content != null
                || FeedbackOverlay.Content != null
                || ClosePromptOverlay.Content != null;
        }

        static MainWindow()
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(200));
        }

        private readonly ITeamOverlayService _teamOverlayService;

        public MainWindow(ITeamOverlayService teamOverlayService)
        {
            _teamOverlayService = teamOverlayService;
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            StateChanged += OnWindowStateChanged;
            Closing += OnWindowClosing;
            _teamOverlayService.NavigateToTeamInfoRequested += OnNavigateToTeamInfoRequested;
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
            // 最大化时移除 resize 边框，避免窗口偏移导致按钮命中测试坐标错位
            if (MainWindowChrome is not null)
            {
                MainWindowChrome.ResizeBorderThickness = WindowState == WindowState.Maximized
                    ? new Thickness(0)
                    : new Thickness(ResizeBorder);
            }
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
            catch (Exception ex)
            {
                // Settings 服务异常会让关闭行为静默回退到"直接退出"，丢失"最小化到托盘"语义；
                // 至少留诊断让用户知道"为什么我设了最小化但点 X 还是直接退出"。
                System.Diagnostics.Debug.WriteLine($"[MainWindow] OnClosing settings resolve failed: {ex.Message}");
            }

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
            // 双击标题栏切换最大化/还原
            if (msg == WM_NCLBUTTONDBLCLK && wParam.ToInt32() == HTCAPTION)
            {
                if (IsAnyOverlayActive()) return IntPtr.Zero;
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                handled = true;
                return IntPtr.Zero;
            }

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

                if (WindowState != WindowState.Maximized)
                {
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
                }

                // Title bar drag area (between resize zone and window buttons)
                if (ptY >= ResizeBorder && ptY <= 32 && ptX > 70 && ptX < w - 100)
                {
                    if (!IsAnyOverlayActive())
                    {
                        handled = true;
                        return (IntPtr)HTCAPTION;
                    }
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
            // 直接终止进程，不执行任何优雅清理。
            // JobObject (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) 保证子进程 (PaddleOCR-json.exe) 被 OS 自动清理。
            System.Diagnostics.Process.GetCurrentProcess().Kill();
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

        
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsAnyOverlayActive()) return;
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                e.Handled = true;
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayMenu_Settings_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
            var navigation = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<IMainContentNavigationService>();
            navigation.NavigateTo(PageNames.SettingsPage);
        }

        private void TrayMenu_Stats_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
            var navigation = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<IMainContentNavigationService>();
            navigation.NavigateTo(PageNames.StatsPage);
        }

        private void TrayMenu_TeamInfo_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
            var navigation = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<IMainContentNavigationService>();
            navigation.NavigateTo(PageNames.TeamInfoPage);
        }

        private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
        {
            // 直接终止进程，不执行任何优雅清理。
            // JobObject (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) 保证子进程 (PaddleOCR-json.exe) 被 OS 自动清理。
            System.Diagnostics.Process.GetCurrentProcess().Kill();
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


        private void TrayMenu_ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ISettingsService>();

            TrayLangZhCN.IsChecked = settings.Current.Language == "zh-CN";
            TrayLangZhTW.IsChecked = settings.Current.Language == "zh-TW";
            TrayLangEn.IsChecked = settings.Current.Language == "en";

            TrayCloseMinToTaskbar.IsChecked = settings.Current.CloseBehavior == "MinimizeToTaskbar";
            TrayCloseExitDirectly.IsChecked = settings.Current.CloseBehavior == "ExitDirectly";

            TrayRememberCloseBehavior.IsChecked = settings.Current.CloseBehaviorRemembered;
            TrayShowTeamOverlay.IsChecked = settings.Current.ShowTeamOverlayDuringHeroSelection;
        }

        private void TrayMenu_Language_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton rb) return;
            if (rb.IsChecked != true) return;

            var localization = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ILocalizationService>();
            var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ISettingsService>();

            var langCode = rb.Name switch
            {
                nameof(TrayLangZhCN) => "zh-CN",
                nameof(TrayLangZhTW) => "zh-TW",
                nameof(TrayLangEn) => "en",
                _ => (string?)null
            };
            if (langCode == null) return;

            localization.ApplyLanguage(langCode);
            localization.CurrentLanguage = langCode;
            settings.Current.Language = langCode;
            _ = settings.SaveAsync();

            TrayLangZhCN.IsChecked = langCode == "zh-CN";
            TrayLangZhTW.IsChecked = langCode == "zh-TW";
            TrayLangEn.IsChecked = langCode == "en";
        }

        private void TrayMenu_CloseBehavior_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton rb) return;

            var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ISettingsService>();

            var behavior = rb.Name switch
            {
                nameof(TrayCloseMinToTaskbar) => "MinimizeToTaskbar",
                nameof(TrayCloseExitDirectly) => "ExitDirectly",
                _ => (string?)null
            };
            if (behavior == null) return;

            settings.Current.CloseBehavior = behavior;
            _ = settings.SaveAsync();

            TrayCloseMinToTaskbar.IsChecked = behavior == "MinimizeToTaskbar";
            TrayCloseExitDirectly.IsChecked = behavior == "ExitDirectly";
        }

        private void TrayMenu_ShowTeamOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox cb) return;
            var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ISettingsService>();
            settings.Current.ShowTeamOverlayDuringHeroSelection = cb.IsChecked == true;
            _ = settings.SaveAsync();
            TrayShowTeamOverlay.IsChecked = settings.Current.ShowTeamOverlayDuringHeroSelection;
        }

        private void TrayMenu_RememberCloseBehavior_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox cb) return;
            var settings = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ISettingsService>();
            settings.Current.CloseBehaviorRemembered = cb.IsChecked == true;
            _ = settings.SaveAsync();
            TrayRememberCloseBehavior.IsChecked = settings.Current.CloseBehaviorRemembered;
        }

        private void TrayMenu_ItemText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement textElement) return;
            if (textElement.Parent is not Grid grid) return;

            foreach (var child in grid.Children)
            {
                if (child is System.Windows.Controls.RadioButton rb)
                {
                    rb.IsChecked = true;
                    rb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    e.Handled = true;
                    return;
                }

                if (child is System.Windows.Controls.CheckBox cb)
                {
                    cb.IsChecked = !cb.IsChecked;
                    cb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    e.Handled = true;
                    return;
                }
            }
        }

        private void OnNavigateToTeamInfoRequested()
        {
            RestoreFromTray();
        }
    }
}