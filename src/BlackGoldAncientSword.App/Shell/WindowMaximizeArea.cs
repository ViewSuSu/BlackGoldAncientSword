using System;
using System.Runtime.InteropServices;

namespace BlackGoldAncientSword.App.Shell
{
    /// <summary>
    /// 无边框窗口（WindowStyle=None）走的是 Windows 默认的最大化算法：按"显示器工作区 + 不可见调整边框"
    /// 给尺寸，再把窗口整体上移/左移，让边框落到屏幕外。有边框的常规窗口看不出来，因为那圈边框本来就是
    /// 非客户区；无边框窗口整块都是可见内容，于是四边各溢出屏幕约 10px——底部状态栏正好落进任务栏区域
    /// 被挡住，顶部标题栏也被切掉一截。
    /// <para>
    /// 拦截 WM_GETMINMAXINFO，把最大化边界钳回显示器工作区。注意这会一并跳过 WPF 自身在该消息里做的
    /// MaxWidth/MaxHeight 钳制，主窗口没有使用这些属性，后续要用需同步到这里。
    /// </para>
    /// </summary>
    internal static class WindowMaximizeArea
    {
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

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

        /// <summary>
        /// 把 lParam 指向的 MINMAXINFO 改成"最大化就是当前显示器的整个工作区"。
        /// 物理像素坐标，只对 DPI 感知进程成立（本项目即如此）。
        /// </summary>
        /// <returns>取不到显示器信息时返回 false，交由 WPF 默认处理。</returns>
        public static bool TryClampMaximizeBounds(IntPtr hwnd, IntPtr lParam)
        {
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref monitorInfo))
                return false;

            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            mmi.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
            mmi.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
            mmi.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
            mmi.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;

            // 拖拽上限也得跟着放宽，否则从最大化往下拖时窗口被卡在工作区尺寸内放不大。
            mmi.ptMaxTrackSize.X = Math.Max(mmi.ptMaxTrackSize.X, mmi.ptMaxSize.X);
            mmi.ptMaxTrackSize.Y = Math.Max(mmi.ptMaxTrackSize.Y, mmi.ptMaxSize.Y);

            Marshal.StructureToPtr(mmi, lParam, false);
            return true;
        }
    }
}
