using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using HCMessageBox = HandyControl.Controls.MessageBox;

namespace BlackGoldAncientSword.Update.Shell
{
    public partial class UpdateWindow : Window
    {
        public event EventHandler? CancelRequested;

        /// <summary>是否已进入不可取消阶段（覆盖文件 / 重启主程序），由 UpdaterRunner 设置。</summary>
        public bool IsCancellable { get; set; } = true;

        /// <summary>主 App 进程 PID（fallback 用），null 则跳过。</summary>
        public int? MainPid { get; set; }

        /// <summary>主 App 主窗口 HWND，最优先的定位源；<see cref="IntPtr.Zero"/> 则退回 PID。</summary>
        public IntPtr MainHwnd { get; set; } = IntPtr.Zero;

        public UpdateWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, e) =>
            {
                // 标题栏拖动：最小化/最大化状态下的按钮点击不应触发 DragMove，否则 Click 事件不会派发。
                if (e.ChangedButton == MouseButton.Left && WindowState == WindowState.Normal)
                    DragMove();
            };
            Closing += UpdateWindow_Closing;
            // ContentRendered 触发时 SizeToContent 已完成，ActualHeight 可靠——此时定位才不会看到窗口从屏幕外瞬移到目标位置的闪烁。
            ContentRendered += (_, _) =>
            {
                CenterOnMainOrScreen();
                HookMainWindowMinimize();
            };
            Closed += (_, _) => UnhookMainWindowMinimize();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int OBJID_WINDOW = 0;

        private IntPtr _minimizeHook = IntPtr.Zero;
        private WinEventDelegate? _minimizeHookProc;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// 挂 EVENT_SYSTEM_MINIMIZESTART / MINIMIZEEND 跟随主 App 主窗口的 minimize 状态。
        /// 挂在主 App 的进程 ID 上（OUT_OF_CONTEXT + idProcess），避免 hook 到系统所有窗口。
        /// 回调在 Updater 自己的 UI 线程执行（OUT_OF_CONTEXT 无 DLL 注入，事件走消息队列）。
        /// </summary>
        private void HookMainWindowMinimize()
        {
            var mainHwnd = ResolveMainHwnd();
            if (mainHwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(mainHwnd, out uint mainPid);
            if (mainPid == 0) return;

            _minimizeHookProc = OnMainWindowMinimizeEvent;
            _minimizeHook = SetWinEventHook(
                EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, _minimizeHookProc,
                mainPid, 0, WINEVENT_OUTOFCONTEXT);
        }

        private void UnhookMainWindowMinimize()
        {
            if (_minimizeHook != IntPtr.Zero)
            {
                UnhookWinEvent(_minimizeHook);
                _minimizeHook = IntPtr.Zero;
                _minimizeHookProc = null;
            }
        }

        private void OnMainWindowMinimizeEvent(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // 只关心窗口对象自身，且必须是主 App 主窗口，防止子窗口 / MessageBox 也触发同步
            if (idObject != OBJID_WINDOW) return;
            if (hwnd != MainHwnd) return;

            // 回调时机跟随主 App 状态：MINIMIZESTART 时同步最小化；MINIMIZEEND 时恢复到 Normal。
            var state = eventType == EVENT_SYSTEM_MINIMIZESTART
                ? WindowState.Minimized
                : WindowState.Normal;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WindowState != state) WindowState = state;
            }));
        }

        /// <summary>
        /// 优先居中于主 App 窗口：HWND > PID → MainWindowHandle > 屏幕中心。
        /// <para>
        /// DPI：<c>GetWindowRect</c> 返回**物理像素**，而 WPF <c>Left/Top</c> 是 DIP。
        /// 直接把物理像素赋给 Left/Top，缩放不是 100% 时就会偏。此处用 <c>GetDpiForWindow</c>
        /// 拿主窗口 DPI，把物理像素换算成 DIP 再定位——PerMonitorV2 下两窗口若在同一显示器 DPI 一致，
        /// 换算结果精确对齐。
        /// </para>
        /// </summary>
        private void CenterOnMainOrScreen()
        {
            var w = ActualWidth > 0 ? ActualWidth : Width;
            var h = ActualHeight > 0 ? ActualHeight : Height;

            var mainHwnd = ResolveMainHwnd();
            if (mainHwnd != IntPtr.Zero && GetWindowRect(mainHwnd, out var rect))
            {
                var mainWpx = rect.Right - rect.Left;
                var mainHpx = rect.Bottom - rect.Top;
                if (mainWpx > 0 && mainHpx > 0)
                {
                    int dpi = GetDpiForWindow(mainHwnd);
                    double scale = dpi > 0 ? dpi / 96.0 : 1.0;

                    // 主窗口在 DIP 坐标系里的位置和尺寸
                    double mainLeftDip = rect.Left / scale;
                    double mainTopDip = rect.Top / scale;
                    double mainWDip = mainWpx / scale;
                    double mainHDip = mainHpx / scale;

                    Left = mainLeftDip + (mainWDip - w) / 2.0;
                    Top = mainTopDip + (mainHDip - h) / 2.0;
                    return;
                }
            }

            // 回退屏幕工作区中心
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - w) / 2.0;
            Top = workArea.Top + (workArea.Height - h) / 2.0;
        }

        private IntPtr ResolveMainHwnd()
        {
            // 首选：主 App 直接传的 HWND
            if (MainHwnd != IntPtr.Zero && IsWindow(MainHwnd) && IsWindowVisible(MainHwnd))
                return MainHwnd;

            // 次选：按 PID 拿。主 App 是 AllowsTransparency + WindowStyle=None 时可能返回 0；
            // 若如此，视为无法定位。
            if (MainPid is int pid)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    var hwnd = proc.MainWindowHandle;
                    if (hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd))
                        return hwnd;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateWindow] ResolveMainHwnd pid={pid} failed: {ex.Message}");
                }
            }
            return IntPtr.Zero;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryConfirmCancel())
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Alt+F4 / 系统关闭也走同样的二次确认
            if (!IsCancellable) return;
            if (_closing) return;
            if (!TryConfirmCancel())
            {
                e.Cancel = true;
                return;
            }
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool _closing;

        /// <summary>给 UpdaterRunner 在最后强制关闭时调用，跳过二次确认。</summary>
        public void ForceClose()
        {
            _closing = true;
            Close();
        }

        private bool TryConfirmCancel()
        {
            if (!IsCancellable) return false;
            var result = HCMessageBox.Show(
                "是否停止更新？\n\n已下载的临时文件会被清除。",
                "停止更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }
    }
}
