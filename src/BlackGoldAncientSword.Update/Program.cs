using System;
using Avalonia;
using BlackGoldAncientSword.Update.Services;

namespace BlackGoldAncientSword.Update
{
    internal static class Program
    {
        private const string FatalCaption = "BlackGoldAncientSword 更新程序";

        [System.STAThread]
        public static int Main(string[] args)
        {
            // 兜底：任何未被 catch 的异常（含其它线程冒泡到进程）都弹原生错误框。
            // 不能依赖 Avalonia ConfirmDialog —— 主循环可能根本没起来。
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    NativeMessageBox.ShowFatal(FatalCaption, ex);
            };

            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                NativeMessageBox.ShowFatal(FatalCaption, ex);
                return -1;
            }
        }

        // Avalonia previewer / 设计期需要此方法
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
