using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Downloader.Services
{
    /// <summary>
    /// 把安装向导窗口强制拉到最前。
    /// 背景：Inno Setup 生成的 Setup.exe 通常 requireAdministrator，触发 UAC 后由 Windows 派生一个新的
    /// 提权进程运行真正的向导，Process.Start 返回的 launcher 进程会立即退出，MainWindowHandle 拿不到。
    /// 所以除了尝试 Process.MainWindowHandle 外，还要枚举所有顶层窗口，按 Inno 特征
    /// （ClassName="TWizardForm" 或标题包含产品名）找到向导窗口并 SetForegroundWindow + Restore + BringToTop。
    /// </summary>
    public static class InstallerForegrounder
    {
        private const uint ASFW_ANY = 0xFFFFFFFF;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new(-2);

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint dwProcessId);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>启动前调用：允许即将启动的子进程 / 提权进程抢占前台焦点。</summary>
        public static void PrepareForegroundHandover()
        {
            try { AllowSetForegroundWindow(ASFW_ANY); }
            catch (Exception ex) { Debug.WriteLine($"[Foregrounder] AllowSetForegroundWindow 失败: {ex.Message}"); }
        }

        /// <summary>
        /// 启动后调用：尝试把安装向导窗口拉到最前。
        /// 三条并行策略，谁先命中就用谁：
        ///   1. launcher Process.MainWindowHandle（非提权 Setup 场景）
        ///   2. Inno Setup 特征窗口类 "TWizardForm"
        ///   3. 窗口标题包含 titleHint（产品名，如 "BlackGoldAncientSword"）
        /// 最多轮询 5 秒。
        /// </summary>
        public static async Task BringInstallerToFrontAsync(Process? launcher, string titleHint, CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                IntPtr hWnd = IntPtr.Zero;

                // 策略 1：launcher.MainWindowHandle（Setup 未提权时能拿到）
                try
                {
                    if (launcher != null && !launcher.HasExited)
                    {
                        launcher.Refresh();
                        if (launcher.MainWindowHandle != IntPtr.Zero && IsWindow(launcher.MainWindowHandle))
                            hWnd = launcher.MainWindowHandle;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[Foregrounder] Refresh launcher 失败: {ex.Message}"); }

                // 策略 2 & 3：枚举顶层窗口找 Inno 特征
                if (hWnd == IntPtr.Zero)
                    hWnd = FindWizardWindow(titleHint);

                if (hWnd != IntPtr.Zero)
                {
                    Debug.WriteLine($"[Foregrounder] 命中向导窗口 hWnd={hWnd}");
                    ForceForeground(hWnd);
                    return;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            Debug.WriteLine("[Foregrounder] 5 秒内未找到安装向导窗口，放弃前置");
        }

        private static IntPtr FindWizardWindow(string titleHint)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                try
                {
                    if (!IsWindowVisible(h)) return true;

                    var cls = new StringBuilder(128);
                    GetClassName(h, cls, cls.Capacity);
                    var clsName = cls.ToString();

                    // Inno Setup 向导窗口固定用 Delphi VCL 的 TWizardForm 类
                    bool isInno = clsName.Equals("TWizardForm", StringComparison.Ordinal);

                    // 兜底：标题含 titleHint 也认（例如 UAC 弹窗关闭后 wizard 出来时）
                    bool titleHit = false;
                    if (!isInno && !string.IsNullOrEmpty(titleHint))
                    {
                        var title = new StringBuilder(256);
                        GetWindowText(h, title, title.Capacity);
                        titleHit = title.ToString().Contains(titleHint, StringComparison.OrdinalIgnoreCase);
                    }

                    if (isInno || titleHit)
                    {
                        result = h;
                        return false;
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static void ForceForeground(IntPtr hWnd)
        {
            try
            {
                ShowWindow(hWnd, SW_RESTORE);
                // 双击 SetForegroundWindow：先 topmost 一下再 non-topmost，破解焦点抢占限制
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                ShowWindow(hWnd, SW_SHOW);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Foregrounder] ForceForeground 失败: {ex.Message}");
            }
        }
    }
}
