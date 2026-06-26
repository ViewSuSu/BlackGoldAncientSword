using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BlackGoldAncientSword.Update.Services;
using BlackGoldAncientSword.Update.Shell;
using BlackGoldAncientSword.Update.ViewModels;

namespace BlackGoldAncientSword.Update
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            try
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    // 没主窗口时也不让 dispatcher 立即收摊（错误对话框关闭前不能退）
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                    var args = desktop.Args ?? Array.Empty<string>();
                    var options = UpdateOptions.Parse(args);

                    if (string.IsNullOrWhiteSpace(options.ZipUrl))
                    {
                        // 必须 Post 到 dispatcher 跑：此刻主循环还未启动，
                        // 同步调用 Shutdown / ShowDialog 都会抛 "Dispatcher shut down"
                        Dispatcher.UIThread.Post(async () =>
                        {
                            await ConfirmDialog.ShowErrorAsync(
                                null,
                                "BlackGoldAncientSword 更新程序",
                                "缺少必需参数 --url <zipUrl>");
                            desktop.Shutdown(1);
                        });
                        return;
                    }

                    var vm = new UpdateViewModel();
                    var window = new UpdateWindow { DataContext = vm };
                    desktop.MainWindow = window;
                    window.Show();

                    var runner = new UpdaterRunner(options, vm, window);
                    _ = runner.RunAsync();

                    desktop.Exit += (_, e) =>
                    {
                        Debug.WriteLine($"[Updater] Exit code={e.ApplicationExitCode}");
                    };
                }

                base.OnFrameworkInitializationCompleted();
            }
            catch (Exception ex)
            {
                // OnFrameworkInitializationCompleted 抛出后 Avalonia 主循环不再可用，
                // 走原生 Win32 MessageBox 把异常告诉用户，并 Shutdown(-1) 退出。
                NativeMessageBox.ShowFatal("BlackGoldAncientSword 更新程序", ex);
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(-1);
                }
            }
        }
    }
}
