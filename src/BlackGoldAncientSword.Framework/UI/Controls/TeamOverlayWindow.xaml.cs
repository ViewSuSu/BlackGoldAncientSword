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

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

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
        private const int MDT_EFFECTIVE_DPI = 0;

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
        /// 在永劫无间游戏窗口所在显示器的右下角定位覆盖层窗口，
        /// 使弹窗右下角与显示器（rcMonitor）右下角完全重合。
        /// <para>
        /// GetMonitorInfo 的 rcMonitor 是物理像素（虚拟屏幕坐标系）。WPF 窗口 Left/Top
        /// 是 DIP 逻辑坐标。换算关系：逻辑 = (物理 - 该显示器物理原点) / 每逻辑像素物理数 + 虚拟屏幕逻辑原点。
        /// 每逻辑像素物理数 = 显示器 DPI / 96（用 GetDpiForMonitor 取该显示器真实 DPI，而非窗口 DPI，
        /// 因为窗口可能尚未 Show，PresentationSource 为 null）。
        /// </para>
        /// </summary>
        public void PositionOnGameMonitor()
        {
            var gameHwnd = FindNarakaWindowHandle();
            var selfHwnd = new WindowInteropHelper(this).Handle;
            var hMonitor = gameHwnd != IntPtr.Zero
                ? MonitorFromWindow(gameHwnd, MONITOR_DEFAULTTONEAREST)
                : MonitorFromWindow(selfHwnd, MONITOR_DEFAULTTONEAREST);

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
                return;

            // 显示器真实 DPI（每逻辑像素对应的物理像素数 = dpi/96）。
            GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);
            if (dpiX == 0) dpiX = 96;
            if (dpiY == 0) dpiY = 96;
            double scaleX = dpiX / 96.0;
            double scaleY = dpiY / 96.0;

            // 测量实际尺寸：Width 固定（420），Height 由内容决定（Min/MaxHeight 区间）。
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            UpdateLayout();
            double w = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? Width : ActualWidth;
            double h = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? MinHeight : ActualHeight;

            // 虚拟屏幕逻辑原点（SystemParameters 已是 DIP）。
            double vScreenLeft = SystemParameters.VirtualScreenLeft;
            double vScreenTop = SystemParameters.VirtualScreenTop;

            // 显示器物理原点相对虚拟屏幕原点的逻辑坐标。
            double monLogicalLeft = vScreenLeft + (monitorInfo.rcMonitor.Left - SystemParameters.VirtualScreenLeft) / scaleX;
            double monLogicalTop = vScreenTop + (monitorInfo.rcMonitor.Top - SystemParameters.VirtualScreenTop) / scaleY;

            // 显示器右下角（物理）→ 逻辑坐标。
            double monLogicalRight = vScreenLeft + (monitorInfo.rcMonitor.Right - SystemParameters.VirtualScreenLeft) / scaleX;
            double monLogicalBottom = vScreenTop + (monitorInfo.rcMonitor.Bottom - SystemParameters.VirtualScreenTop) / scaleY;

            // 弹窗右下角与显示器右下角完全重合。
            double desiredLeft = monLogicalRight - w;
            double desiredTop = monLogicalBottom - h;

            // 防御：窗口不超出显示器左/上边界（仅极端情况，右下角重合不受影响）。
            if (desiredLeft < monLogicalLeft) desiredLeft = monLogicalLeft;
            if (desiredTop < monLogicalTop) desiredTop = monLogicalTop;

            Left = desiredLeft;
            Top = desiredTop;
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
