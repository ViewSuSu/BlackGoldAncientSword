using System.Windows.Threading;
using NarakaBladepoint.StatsAssistant.App.Shell;
using NarakaBladepoint.StatsAssistant.Modules;
using NarakaBladepoint.StatsAssistant.Framework.Core.Events;
using NarakaBladepoint.StatsAssistant.Framework.Services;
using Mapster;

namespace NarakaBladepoint.StatsAssistant.App
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
        }

        private void ConfigureTypeAdapterConfig()
        {
            TypeAdapterConfig.GlobalSettings.Scan(typeof(NarakaBladepoint.StatsAssistant.Modules.Mappings.BattleMappingRegister).Assembly);
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

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            // Wire image cache service to converter
            try
            {
                var cacheService = Container.Resolve<NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions.IImageCacheService>();
                var settingsService = Container.Resolve<NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions.ISettingsService>();
                var cachePath = settingsService.Current.CachePath;
                if (string.IsNullOrEmpty(cachePath))
                    cachePath = NarakaBladepoint.StatsAssistant.Framework.Services.AppSettings.GetDefaultCachePath();
                cacheService.CachePath = cachePath;
                NarakaBladepoint.StatsAssistant.Framework.Core.Extensions.UrlToImageSourceConverter.SetCacheService(cacheService);
            }
            catch { }

            // Configure online update service
            try
            {
                var updateService = Container.Resolve<NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions.IUpdateService>();
                updateService.Configure("https://github.com/ViewSuSu/NarakaBladepoint-Stats-Assistant");
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
                // Silent fail to avoid recursive error loops
            }
        }
    }
}