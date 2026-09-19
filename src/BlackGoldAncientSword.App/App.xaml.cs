using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using System.Diagnostics;
using BlackGoldAncientSword.App.Shell;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Services;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor;
using BlackGoldAncientSword.Modules;
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

            // 版本号权威来源是 App 程序集，须在数据访问层初始化之前注入，
            // 让请求头里的客户端版本取到正确值。
            ClientVersionProvider.Initialize(typeof(App).Assembly);

            // 启动流程契约（顺序不能乱）：
            //   1) 数据访问层 INIT（静默）：只装请求链 + 恢复本机登录态，不弹任何 UI
            //   2) Settings 加载：后续 ImageCache / Update 可能读 settings.Current
            //   3) ImageCache init：CachePath 依赖 Settings
            //   4) 新版本检测 + 更新 gate：await CheckForUpdatesAsync，如有新版则阻塞在 IUpdateGateService.WaitAsync，
            //      直到用户点在线更新 / 打开浏览器 / 稍后再说，DismissOverlay 里 Complete()
            //   5) 登录 gate：本地无有效登录态才弹扫码；用户取消 → Shutdown
            //   6) 主页导航 + OCR 预热

            // [1] 数据访问层 INIT。任何构造失败都不能中断 App 启动，只是让客户端保持未登录态。
            IAuthChallengeService? challengeService = null;
            IHeyboxSessionState? sessionStateForGate = null;
            try
            {
                var sessionState = Container.Resolve<IHeyboxSessionState>();
                var sessionStore = Container.Resolve<IHeyboxSessionStore>();
                var challenge = Container.Resolve<IAuthChallengeService>();
                challengeService = challenge;
                sessionStateForGate = sessionState;

                NarakaApiClient.Configure(new HeyboxRequestPacingHandler
                {
                    InnerHandler = new HeyboxSignatureHandler(sessionState)
                    {
                        InnerHandler = new HttpClientHandler { UseCookies = false }
                    },
                });

                var restored = sessionStore.Load();
                var restoredAlreadyChecked = false;
#if DEBUG
                // Debug 下：本机开发用的会话优先（固定用测试账号调试），取不到时才沿用本地存档里登录后的会话。
                var debugSession = await LoadDebugSessionAsync();
                restored = debugSession ?? restored;
                restoredAlreadyChecked = debugSession != null;

                // 用了本机调试凭证才单独校验一次（失效就回登录页），避免和下面存档校验重复打接口。
                if (debugSession != null && !await IsSessionAliveAsync().ConfigureAwait(false))
                {
                    AppLog.Info(nameof(App), "debug session rejected by server, falling back to login challenge");
                    restored = null;
                }
#endif
                // 本地存档恢复的登录态也要校验（Debug / Release 都做）：服务端返回的
                // token/pkey 可能已过期或被踢，不校验的话启动照样当已登录，要等查战绩时
                // 接口回 status:login 才暴露，且不会自动重新登录。代价是启动多打一个接口。
                if (restored != null && !restoredAlreadyChecked)
                {
                    var alive = await IsSessionAliveAsync().ConfigureAwait(false);
                    if (!alive)
                    {
                        AppLog.Info(nameof(App), "stored heybox session rejected by server, clearing local store");
                        sessionStore.Clear();
                        restored = null;
                    }
                }

                if (restored != null)
                    sessionState.Set(HeyboxLoginState.FromSession(restored));
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(App), "Heybox pipeline init failed");
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
            if (challengeService != null && sessionStateForGate?.Current is null)
            {
                AppLog.Info(nameof(App), "no heybox session, showing login challenge");
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

        /// <summary>
        /// 本地登录态是否还活着：打一次玩家主页接口，被服务端判定为 login/relogin 就当失效。
        /// 没有本地角色 ID 或请求异常时不拦截（按"活着"处理），避免误伤正常登录用户。
        /// </summary>
        private async Task<bool> IsSessionAliveAsync()
        {
            try
            {
                var prefs = Container.Resolve<IPlayerPrefsService>();
                if (!prefs.Current.IsLoaded) await prefs.LoadAsync().ConfigureAwait(false);

                return await HeyboxSessionProbe
                    .IsSessionAliveAsync(prefs.Current.PlayerId)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                return true;
            }
        }

#if DEBUG
        private static async Task<HeyboxSession?> LoadDebugSessionAsync()
        {
            var raw = Environment.GetEnvironmentVariable("HEYBOX_SESSION");
            if (string.IsNullOrWhiteSpace(raw))
                raw = await ReadLocalSessionFileAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var heyboxId = root.TryGetProperty("heybox_id", out var id) ? id.GetString() : null;
                var pkey = root.TryGetProperty("pkey", out var key) ? key.GetString() : null;
                if (string.IsNullOrEmpty(heyboxId) || string.IsNullOrEmpty(pkey)) return null;

                AppLog.Info(nameof(App), "debug session picked up from environment or local file");
                return new HeyboxSession(heyboxId!, pkey!);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        private static async Task<string?> ReadLocalSessionFileAsync()
        {
            var path = ResolveLocalSessionPath();
            if (path is null || !File.Exists(path)) return null;

            try
            {
                return await File.ReadAllTextAsync(path).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string? ResolveLocalSessionPath()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return Path.Combine(dir.FullName, ".claude", "local-session.json");
            }
            return null;
        }
#endif

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



