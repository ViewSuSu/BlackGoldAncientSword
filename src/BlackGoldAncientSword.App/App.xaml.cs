using System.Windows.Threading;
using System.Diagnostics;
using BlackGoldAncientSword.App.Shell;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services;
using BlackGoldAncientSword.GameMonitor;
using BlackGoldAncientSword.Modules;
using BlackGoldAncientSword.Ocr;
using BlackGoldAncientSword.ScreenCapture;
using Mapster;

namespace BlackGoldAncientSword.App
{
    public partial class App : Framework.Core.Bases.PrismApplicationBase
    {
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.Register<MainWindow>();
            containerRegistry.Register<MainWindowViewModel>();
            containerRegistry.RegisterFrameworkServices();
            containerRegistry.RegisterAppLayer();
            containerRegistry.RegisterModuleLayer();
            containerRegistry.RegisterGameMonitorLayer();
            containerRegistry.RegisterOcrLayer();
            containerRegistry.RegisterScreenCaptureLayer();
        }

        private void ConfigureTypeAdapterConfig()
        {
            TypeAdapterConfig.GlobalSettings.Scan(typeof(BlackGoldAncientSword.Modules.Mappings.BattleMappingRegister).Assembly);
        }

        protected override System.Windows.Window CreateShellExecute()
        {
            ConfigureTypeAdapterConfig();
            return Container.Resolve<MainWindow>();
        }

        protected override IModuleCatalog CreateModuleCatalog()
        {
            return ModuleCatalogConfigManager.ConfigAll();
        }

        protected override async void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var cacheService = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IImageCacheService>();
                var settingsService = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.ISettingsService>();
                var cachePath = settingsService.Current.CachePath;
                if (string.IsNullOrEmpty(cachePath))
                    cachePath = BlackGoldAncientSword.Framework.Services.AppSettings.GetDefaultCachePath();
                cacheService.CachePath = cachePath;
                BlackGoldAncientSword.Framework.Core.Extensions.UrlToImageSourceConverter.SetCacheService(cacheService);
            }
            catch { }

            try
            {
                var settings = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.ISettingsService>();
                // 等待异步加载完成，不阻塞 UI 线程
                await settings.LoadAsync();
                var updater = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService>();
                updater.SetAutoPopupEnabled(settings.Current.AutoCheckUpdates);
                updater.CheckForUpdatesAsync(showNoUpdateMessage: false).SafeFireAndForget("App.CheckForUpdates");
            }
            catch { }

            DispatcherUnhandledException += (_, args) =>
            {
                args.Handled = true;
                PublishError(args.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    Current.Dispatcher.Invoke(() => PublishError(ex));
                }
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                args.SetObserved();
                Current.Dispatcher.Invoke(() => PublishError(args.Exception));
            };
        }

        private void PublishError(Exception ex)
        {
            try
            {
                var aggregator = Container.Resolve<IEventAggregator>();
                aggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(
                        string.Format(
                            System.Windows.Application.Current?.TryFindResource("App.UnhandledError") as string ?? "Error: {0}",
                            ex.Message),
                        new List<string> { "Error" }));
            }
            catch
            {
            }
        }
    }
}


