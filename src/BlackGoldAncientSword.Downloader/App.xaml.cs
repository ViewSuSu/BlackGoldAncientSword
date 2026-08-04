using System.Diagnostics;
using System.Windows;
using BlackGoldAncientSword.Downloader.Infrastructure;
using BlackGoldAncientSword.Downloader.Services;
using BlackGoldAncientSword.Downloader.Shell;
using BlackGoldAncientSword.Downloader.ViewModels;

namespace BlackGoldAncientSword.Downloader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ProcLog.Initialize();
            ProcLog.Info(nameof(App), "downloader started");

            var vm = new DownloadViewModel();
            var window = new DownloadWindow { DataContext = vm };
            MainWindow = window;
            window.Show();

            var runner = new DownloaderRunner(vm, window);
            _ = runner.RunAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine($"[Downloader] OnExit code={e.ApplicationExitCode}");
            base.OnExit(e);
        }
    }
}
