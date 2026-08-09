using System.Net.Http;
using System.Windows.Threading;
using System.Diagnostics;
using BlackGoldAncientSword.App.Shell;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using BlackGoldAncientSword.Framework.Services;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor;
using BlackGoldAncientSword.Modules;
using Mapster;

namespace BlackGoldAncientSword.App
{
    public partial class App : Framework.Core.Bases.PrismApplicationBase
    {
        private AuthTokenExpiryMonitor? _authTokenExpiryMonitor;

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.Register<MainWindow>();
            containerRegistry.Register<MainWindowViewModel>();
            containerRegistry.RegisterFrameworkServices();
            containerRegistry.RegisterAppLayer();
            containerRegistry.RegisterModuleLayer();
            containerRegistry.RegisterGameMonitorLayer();
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

            // 启动流程契约（顺序不能乱）：
            //   1) Auth pipeline INIT（静默）：只搭 handler + 恢复本地 token + 启动过期监视，不弹任何 UI
            //   2) Settings 加载：后续 ImageCache / Update 可能读 settings.Current
            //   3) ImageCache init：CachePath 依赖 Settings
            //   4) 新版本检测 + 更新 gate：await CheckForUpdatesAsync，如有新版则阻塞在 IUpdateGateService.WaitAsync，
            //      直到用户点在线更新 / 打开浏览器 / 稍后再说，DismissOverlay 里 Complete()
            //   5) 登录 gate：本地无有效 token 才弹扫码；用户取消 → Shutdown
            //   6) 主页导航 + OCR 预热

            // [1] Auth pipeline INIT。任何构造失败都不能中断 App 启动，只是让客户端保持无签名/无 Bearer。
            IAuthChallengeService? challengeService = null;
            IAuthTokenState? tokenStateForGate = null;
            try
            {
                var ticketProvider = Container.Resolve<ISignatureTicketProvider>();
                var tokenState = Container.Resolve<IAuthTokenState>();
                var tokenStore = Container.Resolve<IAuthTokenStore>();
                var refresher = Container.Resolve<IAuthTokenRefresher>();
                var challenge = Container.Resolve<IAuthChallengeService>();
                challengeService = challenge;
                tokenStateForGate = tokenState;

                var handlerChain = new SignatureHandler(ticketProvider)
                {
                    InnerHandler = new AuthTokenHandler(tokenState, tokenStore, refresher, challenge)
                    {
                        InnerHandler = new HttpClientHandler()
                    }
                };
                NarakaApiClient.Configure(handlerChain);

                var restored = tokenStore.Load();
                if (restored != null && restored.ExpiresAtUnixMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                {
                    tokenState.Set(restored);
                }
                else if (restored != null)
                {
                    tokenStore.Clear();
                }

                // 主动过期监视：token 到期前 30s 提前 refresh；失败自动弹登录。
                _authTokenExpiryMonitor = new AuthTokenExpiryMonitor(tokenState, tokenStore, refresher, challenge);
                _authTokenExpiryMonitor.Start();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(App), "Auth pipeline init failed");
            }

            // [2] Settings 加载。失败留默认值 + 日志。
            BlackGoldAncientSword.Framework.Services.Abstractions.ISettingsService? settings = null;
            try
            {
                settings = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.ISettingsService>();
                await settings.LoadAsync();
                // Settings 就绪后立即初始化本地日志（Release 才落盘），后续启动步骤的 catch 才有 sink 可写。
                var logPath = settings.Current.LogPath;
                if (string.IsNullOrWhiteSpace(logPath))
                    logPath = BlackGoldAncientSword.Framework.Services.AppSettings.GetDefaultLogPath();
                AppLog.Initialize(logPath);
                // 日志就绪后的第一条：标记本次启动，作为排查"卡在启动"时的时间锚点。
                AppLog.Info($"{nameof(App)}.OnStartup", "app started, log ready; entering startup pipeline");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(App)}.OnStartup", "Settings load failed");
            }

            // [2.5] 字号缩放应用。依赖 Settings；在首帧渲染前写入资源，保证默认字号即正确。
            try
            {
                var uiScale = Container.Resolve<IUiScaleService>();
                uiScale.Apply(settings?.Current.FontScale ?? 0);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(App), "Font scale apply failed");
            }

            // [3] ImageCache init。依赖 Settings。
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
                AppLog.Error(ex, nameof(App), "ImageCache init failed");
            }

            // [4] 新版本检测 + 更新 gate。await 检测完成，命中新版就阻塞等用户处理完弹窗。
            //     MainWindowViewModel 订阅 UpdateAvailabilityChanged → 自动弹 UpdateNotificationRegion 卡片；
            //     用户三选一（在线更新 / 打开浏览器 / 稍后）→ DismissOverlay → IUpdateGateService.Complete()。
            //     检测失败或无新版 → 直接跳过 WaitAsync，进入登录 gate。
            //
            //     StartupGate：整个 [4] 期间 MainWindow 顶层遮罩阻拦一切 UI 操作，await 结束后 finally 释放遮罩，
            //     由后续的 update overlay / auth challenge overlay 接管遮挡。放 finally 是为了检测抛异常也能放行，
            //     否则用户会永远看到"正在检查更新…"卡在屏幕上。
            var startupGate = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IStartupGateService>();
            var updateGate = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateGateService>();
            try
            {
                var updater = Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService>();
                // UpdateService.CheckForUpdatesAsync 内部 await SafeInvokeAsync，保证返回时 IsUpdateAvailable 属性已同步到最新值。
                // 若改回 fire-and-forget 会导致这里读到过期 false，误判为"无新版" → finally 提前释放 updateGate
                // → 后续 [5] challenge.ShowAsync 里 await updateGate.WaitAsync 立即返回 → challenge overlay 抢在
                // update overlay 之前显示，两个 overlay 会同时冒出来。
                await updater.CheckForUpdatesAsync(showNoUpdateMessage: false);
                AppLog.Info(nameof(App), $"update check done, available={updater.IsUpdateAvailable}");
                if (updater.IsUpdateAvailable)
                {
                    // 检测出新版：先释放启动遮罩让 UpdateNotification overlay 能被用户看清并操作。
                    // updateGate 由 UpdateNotificationPageViewModel.DismissOverlay → Complete() 释放，
                    // 期间 AuthChallengeService.ShowAsync 会 await 它，保证登录弹窗不会挤在更新弹窗之上。
                    startupGate.Complete();
                    await updateGate.WaitAsync();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(App), "Update check / gate failed");
            }
            finally
            {
                // startupGate.Complete 幂等；这里兜底保证遮罩一定放行。
                startupGate.Complete();
                // updateGate 也一定放行：无新版 / 异常时用户完全没看到弹窗，也要放行 challenge，否则
                // 未登录用户永远看不到扫码 overlay（AuthChallengeService 里 await 会永挂）。
                updateGate.Complete();
            }

            // [5] 登录 gate：本地无有效 token 才弹扫码。登录失败 / 用户取消 → Shutdown。
            if (challengeService != null && tokenStateForGate?.Current is null)
            {
                AppLog.Info(nameof(App), "no valid token, showing login challenge");
                bool loggedIn = false;
                try
                {
                    loggedIn = await challengeService.ShowAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, nameof(App), "Startup login gate failed");
                }
                if (!loggedIn)
                {
                    AppLog.Info(nameof(App), "login not completed, shutting down");
                    Shutdown();
                    return;
                }
            }

            // [6] 主页导航。Eagerly create TeamInfo ViewModel so it can always listen for game status.
            AppLog.Info(nameof(App), "startup pipeline done, navigating to home");
            var navigation = Container.Resolve<IMainContentNavigationService>();
            navigation.NavigateTo(PageNames.TeamInfoPage);
            navigation.NavigateTo(PageNames.HomePage);

            // [7] 后台版本轮询：启动期若未发现新版，则每 30s 静默复查一次，发现新版即自动停表，
            // 由 MainWindowViewModel 订阅的 UpdateAvailabilityChanged 点亮左下角"发现新版本"（后台来源不弹卡片）。
            try
            {
                Container.Resolve<BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService>()
                    .StartBackgroundPolling();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(App), "start background update polling failed");
            }
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            // 反订阅顺序与 OnStartup 中订阅顺序保持一致：
            // DispatcherUnhandledException -> AppDomain.UnhandledException -> TaskScheduler.UnobservedTaskException
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;

            _authTokenExpiryMonitor?.Dispose();
            _authTokenExpiryMonitor = null;

            // 刷新 Async 日志队列并释放文件句柄，避免退出时丢失尾部日志。
            AppLog.Shutdown();

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
                AppLog.Error(publishEx, $"{nameof(App)}.{nameof(PublishError)}");
            }
        }
    }
}



