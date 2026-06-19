using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    public partial class TeamOverlayWindow : Window
    {
        private readonly TeamOverlayViewModel _viewModel;

        #region Win32 Interop

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        #endregion

        public TeamOverlayWindow(TeamOverlayViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            InitializeComponent();

            WeakEventManager<Window, EventArgs>.AddHandler(
                this, nameof(SourceInitialized), OnSourceInitialized);
            WeakEventManager<TeamOverlayViewModel, EventArgs>.AddHandler(
                _viewModel, nameof(TeamOverlayViewModel.CloseRequested), OnCloseRequested);
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            PositionOnGameMonitor();
            WeakEventManager<Window, EventArgs>.AddHandler(
                this, nameof(LocationChanged), OnLocationChanged);
        }

        /// <summary>
        /// 在永劫无间游戏窗口所在显示器的右下角定位覆盖层窗口。
        /// </summary>
        public void PositionOnGameMonitor()
        {
            var gameHwnd = FindNarakaWindowHandle();
            var hMonitor = gameHwnd != IntPtr.Zero
                ? MonitorFromWindow(gameHwnd, MONITOR_DEFAULTTONEAREST)
                : MonitorFromWindow(new WindowInteropHelper(this).Handle, MONITOR_DEFAULTTONEAREST);

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
                return;

            var workArea = monitorInfo.rcWork;

            var source = PresentationSource.FromVisual(this);
            var dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new Rect(0, 0, Width, Height));
            UpdateLayout();

            Left = (workArea.Right / dpiScaleX) - ActualWidth - 8;
            Top = (workArea.Bottom / dpiScaleY) - ActualHeight - 8;
        }

        private static IntPtr FindNarakaWindowHandle()
        {
            var procs = Process.GetProcessesByName("NarakaBladepoint");
            try
            {
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        return p.MainWindowHandle;
                }
                return IntPtr.Zero;
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }

        /// <summary>
        /// 标题栏鼠标按下时拖动窗口。
        /// </summary>
        private void TitleBarGrid_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnCloseRequested(object? sender, EventArgs e)
        {
            Hide();
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
        }
    }
}
