using System;
using System.Runtime.InteropServices;

namespace BlackGoldAncientSword.Update.Services
{
    /// <summary>
    /// Avalonia 未启动 / 已崩时使用的原生 Win32 MessageBox。
    /// 不能依赖 Avalonia 的 ConfirmDialog（那需要主循环），
    /// 这里直接 P/Invoke user32!MessageBoxW 显示模态错误窗。
    /// </summary>
    internal static class NativeMessageBox
    {
        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONERROR = 0x00000010;
        private const uint MB_TOPMOST = 0x00040000;
        private const uint MB_SETFOREGROUND = 0x00010000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public static void ShowFatal(string caption, Exception ex)
        {
            var text =
                "在线更新程序启动失败。\n\n" +
                $"异常类型: {ex.GetType().FullName}\n" +
                $"异常消息: {ex.Message}\n\n" +
                $"堆栈:\n{ex.StackTrace}";

            // 嵌套异常一并展示
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 3)
            {
                text +=
                    $"\n\n--- 内部异常 #{depth + 1} ---\n" +
                    $"类型: {inner.GetType().FullName}\n" +
                    $"消息: {inner.Message}";
                inner = inner.InnerException;
                depth++;
            }

            MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
        }

        public static void ShowError(string caption, string message)
        {
            MessageBoxW(IntPtr.Zero, message, caption, MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
        }
    }
}
