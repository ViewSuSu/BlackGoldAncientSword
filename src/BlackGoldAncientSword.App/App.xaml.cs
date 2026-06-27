using System.Windows.Threading;
using System.Diagnostics;
using BlackGoldAncientSword.App.Shell;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
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
            // 异常处理器**必须在 base.OnStartup 之前**注册：
            // 1) base.OnStartup 内部已经会创建 Shell、解析容器，任一步抛异常都需要被接管；
            // 2) 后续 await 之后的续接如果在 UI 线程抛，依赖 DispatcherUnhandledException 才能上报；
            // 3) 移到首部前注册前的早期异常会让 async void 闪退且无任何日志。
            // 订阅顺序：DispatcherUnhandledException -> AppDomain.UnhandledException -> TaskScheduler.UnobservedTaskException
            // 反订阅顺序在 OnExit 中按相同顺序执行（lambda 不可 -=，故全部提取为命名方法）。
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

            base.OnStartup(e);

            // Eagerly create TeamInfo ViewModel so it can always listen for game status
            var navigation = Container.Resolve<IMainContentNavigationService>();
            navigation.NavigateTo(PageNames.TeamInfoPage);
            navigation.NavigateTo(PageNames.HomePage);

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
            catch (Exception ex)
            {
                // 启动期 ImageCache/Settings 初始化失败：后续所有依赖图片缓存的视图都会异常，必须留诊断线索。
                Debug.WriteLine($"[{nameof(App)}] ImageCache init failed: {ex}");
            }

            try
            {
                var settings = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.ISettingsService>();
                // 等待异步加载完成，不阻塞 UI 线程
                await settings.LoadAsync();
                var updater = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService>();

                updater.CheckForUpdatesAsync(showNoUpdateMessage: false).SafeFireAndForget("App.CheckForUpdates");

            }
            catch (Exception ex)
            {
                // 设置加载失败：所有依赖 settings.Current 的模块都会用默认值；至少留日志方便诊断"为什么我的设置没生效"。
                Debug.WriteLine($"[{nameof(App)}] Settings load / update check init failed: {ex}");
            }
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            // 反订阅顺序与 OnStartup 中订阅顺序保持一致：
            // DispatcherUnhandledException -> AppDomain.UnhandledException -> TaskScheduler.UnobservedTaskException
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;

            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            args.Handled = true;
            PublishError(args.Exception);
        }

        private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        {
            if (args.ExceptionObject is Exception ex)
            {
                // 使用 BeginInvoke 避免在 Dispatcher 关闭路径上同步等待导致死锁。
                Current?.Dispatcher.BeginInvoke(() => PublishError(ex));
            }
        }

        private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            args.SetObserved();
            Current?.Dispatcher.BeginInvoke(() => PublishError(args.Exception));
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
            catch (Exception publishEx)
            {
                // PublishError 自身失败属于"异常处理器又抛了异常"的极端路径：不能再上抛或重入。
                // 至少留一条诊断线索，避免静默吞掉根因。
                Debug.WriteLine($"[{nameof(App)}.{nameof(PublishError)}] {publishEx}");
            }
        }
    }
}



