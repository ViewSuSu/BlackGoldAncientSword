using System;
using System.Diagnostics;
using System.Windows;
using BlackGoldAncientSword.Update.Services;
using BlackGoldAncientSword.Update.Shell;
using BlackGoldAncientSword.Update.ViewModels;

namespace BlackGoldAncientSword.Update
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var options = UpdateOptions.Parse(e.Args);
            if (string.IsNullOrWhiteSpace(options.ZipUrl))
            {
                MessageBox.Show(
                    "缺少必需参数 --url <zipUrl>",
                    "BlackGoldAncientSword 更新程序",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var vm = new UpdateViewModel();
            var window = new UpdateWindow { DataContext = vm };
            MainWindow = window;
            window.Show();

            var runner = new UpdaterRunner(options, vm, window);
            _ = runner.RunAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine($"[Updater] OnExit code={e.ApplicationExitCode}");
            base.OnExit(e);
        }
    }
}
