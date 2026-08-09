using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using Prism.Regions;

namespace BlackGoldAncientSword.App.Shell
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IPlayerPrefsService _playerPrefsService;
        private readonly IMainContentNavigationService _navigation;
        private readonly IRegionManager _regionManager;
        private readonly IModuleManager _moduleManager;
        private readonly BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService _updateService;
        private readonly BlackGoldAncientSword.Framework.Services.Abstractions.ILocalizationService _localization;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly IGameStatusMonitor _gameStatusMonitor;
        private readonly IGameLogMonitor _gameLogMonitor;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly ToastQueueManager _toastQueueManager;
        private readonly IStartupGateService _startupGate;
        private readonly DateTime _startupTime = DateTime.UtcNow;

        /// <summary>
        /// true = 启动流程（含更新检测）未完成，MainWindow 顶层遮罩显示，拦截一切 UI 操作。
        /// 由 <see cref="IStartupGateService"/> 的 <see cref="IStartupGateService.BusyChanged"/> 驱动，
        /// 只会从 true 翻到 false。
        /// </summary>
        private bool _isStartupBusy;
        public bool IsStartupBusy
        {
            get => _isStartupBusy;
            set
            {
                if (_isStartupBusy == value) return;
                _isStartupBusy = value;
                RaisePropertyChanged(nameof(IsStartupBusy));
            }
        }

        /// <summary>
        /// true = 用户已点击"在线更新"，Updater 独立进程正在下载/解压/覆盖，主 App 应完全锁死等待被 kill 后重启。
        /// 由 <see cref="OnlineUpdatingStartedEvent"/> 触发，无退出路径——直到 Updater 结束本进程为止。
        /// </summary>
        private bool _isOnlineUpdating;
        public bool IsOnlineUpdating
        {
            get => _isOnlineUpdating;
            set
            {
                if (_isOnlineUpdating == value) return;
                _isOnlineUpdating = value;
                RaisePropertyChanged(nameof(IsOnlineUpdating));
            }
        }
        private bool _isContactPopupOpen;
        public bool IsContactPopupOpen
        {
            get => _isContactPopupOpen;
            set
            {
                if (_isContactPopupOpen == value) return;
                _isContactPopupOpen = value;
                RaisePropertyChanged(nameof(IsContactPopupOpen));
            }
        }

        /// <summary>
        /// Toast 集合由 <see cref="ToastQueueManager"/> 持有，VM 仅做绑定转发，
        /// 保持 XAML `{Binding ToastItems}` 与 code-behind `vm.ToastItems.Remove(item)` 路径不变。
        /// </summary>
        public ObservableCollection<ToastItem> ToastItems => _toastQueueManager.Items;

        private string _activePage = string.Empty;
        public string ActivePage
        {
            get => _activePage;
            set
            {
                if (_activePage == value) return;
                _activePage = value;
                RaisePropertyChanged(nameof(ActivePage));
            }
        }

        private GameStatus _currentGameStatus = GameStatus.Unknown;
        private string _gameStatusText = string.Empty;
        public string GameStatusText
        {
            get => _gameStatusText;
            set
            {
                if (_gameStatusText == value) return;
                _gameStatusText = value;
                RaisePropertyChanged(nameof(GameStatusText));
            }
        }

        private DelegateCommand? _navigateToHomeCommand;
        public DelegateCommand NavigateToHomeCommand =>
            _navigateToHomeCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.HomePage);
            });

        private DelegateCommand? _navigateToStatsCommand;
        public DelegateCommand NavigateToStatsCommand =>
            _navigateToStatsCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.StatsPage);
            });

        private DelegateCommand? _navigateToTeamInfoCommand;
        public DelegateCommand NavigateToTeamInfoCommand =>
            _navigateToTeamInfoCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.TeamInfoPage);
            });

        private DelegateCommand? _navigateToTestTrioCommand;
        public DelegateCommand NavigateToTestTrioCommand =>
            _navigateToTestTrioCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.TestTrioPage);
            });

        private DelegateCommand? _navigateToTestDuoCommand;
        public DelegateCommand NavigateToTestDuoCommand =>
            _navigateToTestDuoCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.TestDuoPage);
            });

        /// <summary>
        /// 仅 Debug 构建为 true，Release 恒为 false。
        /// 用于 XAML 中把测试页入口（测试三排/测试双排）绑定 Visibility，
        /// 保证 Release 下完全不出现，满足"Debug-only 入口"需求。
        /// </summary>
        public bool IsDebugBuild
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        private DelegateCommand? _navigateToSearchCommand;
        public DelegateCommand NavigateToSearchCommand =>
            _navigateToSearchCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.SearchPage);
            });

        private DelegateCommand? _openFeedbackCommand;
        public DelegateCommand OpenFeedbackCommand =>
            _openFeedbackCommand ??= new DelegateCommand(() =>
            {
                EnsureModuleLoaded(PageNames.FeedbackPage);
                _regionManager.RequestNavigate(GlobalConstant.FeedbackRegion, PageNames.FeedbackPage);
            });

        private DelegateCommand? _openSponsorCommand;
        public DelegateCommand OpenSponsorCommand =>
            _openSponsorCommand ??= new DelegateCommand(() =>
            {
                EnsureModuleLoaded(PageNames.SponsorPage);
                _regionManager.RequestNavigate(GlobalConstant.SponsorRegion, PageNames.SponsorPage);
            });

        private DelegateCommand? _openUpdateLogCommand;
        public DelegateCommand OpenUpdateLogCommand =>
            _openUpdateLogCommand ??= new DelegateCommand(() =>
            {
                EnsureModuleLoaded(PageNames.UpdateLogPage);
                _regionManager.RequestNavigate(GlobalConstant.UpdateLogRegion, PageNames.UpdateLogPage);
            });

        private DelegateCommand? _navigateToAnnouncementCommand;

        public string CurrentVersionText =>
            string.Format(
                _localizedText.Get("Settings.CurrentVersion", "{0}"),
                _updateService.CurrentVersion);

        private bool _updateCheckCompleted;
        private bool _updateNotificationShown;
        private bool _isCheckingForUpdate;

        /// <summary>
        /// true = 用户点击"发现新版本"后，正在强制重查远端版本，MainWindow 显示"正在检查更新"转圈浮层。
        /// 检查完成（成功或失败）后由命令的 finally 复位，期间浮层拦截一切点击，防止重复触发。
        /// </summary>
        public bool IsCheckingForUpdate
        {
            get => _isCheckingForUpdate;
            set
            {
                if (_isCheckingForUpdate == value) return;
                _isCheckingForUpdate = value;
                RaisePropertyChanged(nameof(IsCheckingForUpdate));
            }
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set
            {
                if (_isUpdateAvailable == value) return;
                _isUpdateAvailable = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsLatestVersion));
            }
        }

        public bool IsLatestVersion => _updateCheckCompleted && !IsUpdateAvailable;


        public bool CanGoBack => _navigation.CanGoBack;
        private bool _canNavigateToPersonal;
        public bool CanNavigateToPersonal
        {
            get => _canNavigateToPersonal;
            set
            {
                if (_canNavigateToPersonal == value) return;
                _canNavigateToPersonal = value;
                RaisePropertyChanged(nameof(CanNavigateToPersonal));
            }
        }

        private DelegateCommand? _goBackCommand;
        public DelegateCommand GoBackCommand =>
            _goBackCommand ??= new DelegateCommand(() =>
            {
                _navigation.GoBack();
            }).ObservesCanExecute(() => CanGoBack);

        private DelegateCommand? _navigateToPersonalCommand;
        public DelegateCommand NavigateToPersonalCommand =>
            _navigateToPersonalCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.StatsPage);
            });

        public DelegateCommand NavigateToAnnouncementCommand =>
            _navigateToAnnouncementCommand ??= new DelegateCommand(() =>
            {
                EnsureModuleLoaded(PageNames.AnnouncementPage);
                _regionManager.RequestNavigate(GlobalConstant.AnnouncementRegion, PageNames.AnnouncementPage);
            });

        private DelegateCommand? _openSettingsCommand;
        public DelegateCommand OpenSettingsCommand =>
            _openSettingsCommand ??= new DelegateCommand(() =>
            {
                _navigation.NavigateTo(PageNames.SettingsPage);
            });

        private DelegateCommand? _checkForUpdatesCommand;
        public DelegateCommand CheckForUpdatesCommand =>
            _checkForUpdatesCommand ??= new DelegateCommand(async () =>
            {
                // 左下角"发现新版本"点击 → 先弹"正在检查更新"转圈浮层，重查远端最新 tag 并刷新下载 URL，
                // 检查完成后再弹更新卡片。用户主动点击：忽略 _updateNotificationShown 守卫，以便关闭后还能再次打开。
                //
                // 不能用会话内缓存：首次检出新版后后台轮询即停止（UpdateService.OnPollingTick → StopPolling），
                // 若用户长时间登录而远端已发布多个新版本，LatestVersion / ZipDownloadUrl 仍停留在旧 tag；
                // 而 Gitee 同步会清空历史 tag 附件，旧 tag 的 zip 直链 404，直接弹卡片会拿到失效的下载地址。
                try
                {
                    if (IsCheckingForUpdate) return;
                    IsCheckingForUpdate = true;

                    // 先置守卫：重查命中新版时 CheckForUpdatesAsync 内部会触发 UpdateAvailabilityChanged(true)，
                    // 事件驱动的 TryShowUpdateNotification 会再弹一次卡片，与下方显式导航重复。用户已主动点击
                    // 按钮，后续只走命令内的显式导航，抑制事件驱动弹卡。
                    _updateNotificationShown = true;

                    await _updateService.CheckForUpdatesAsync(showNoUpdateMessage: false, UpdateCheckSource.UserManual);
                    // 重查成功且仍确认有新版才弹卡片。瞬时网络失败时 CheckForUpdatesAsync 保留原可用状态，
                    // 这里保持卡片关闭不打断用户；左下角"发现新版本"指示（IsUpdateAvailable）依然可见可重试。
                    if (!_updateService.IsUpdateAvailable)
                        return;

                    EnsureModuleLoaded(PageNames.UpdateNotificationPage);
                    _regionManager.RequestNavigate(
                        GlobalConstant.UpdateNotificationRegion,
                        PageNames.UpdateNotificationPage);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(MainWindowViewModel)}.{nameof(CheckForUpdatesCommand)}", "弹出更新通知失败");
                }
                finally
                {
                    IsCheckingForUpdate = false;
                }
            });

        public UserProfileViewModel UserProfile { get; }

        public MainWindowViewModel(
            IPlayerPrefsService playerPrefsService,
            IMainContentNavigationService navigation,
            IRegionManager regionManager,
            IModuleManager moduleManager,
            BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService updateService,
            BlackGoldAncientSword.Framework.Services.Abstractions.ILocalizationService localizationService,
            ILocalizedTextProvider localizedText,
            IGameStatusMonitor gameStatusMonitor,
            IGameLogMonitor gameLogMonitor,
            IUIDispatcher uiDispatcher,
            ToastQueueManager toastQueueManager,
            UserProfileViewModel userProfile,
            IStartupGateService startupGate)
        {
            UserProfile = userProfile;
            _playerPrefsService = playerPrefsService;
            _navigation = navigation;
            _regionManager = regionManager;
            _moduleManager = moduleManager;
            _updateService = updateService;
            Debug.WriteLine($"[MainWindowVM] UpdateService 已注入，当前版本: {_updateService.CurrentVersion}");
            _localization = localizationService;
            _localizedText = localizedText;
            _gameStatusMonitor = gameStatusMonitor;
            _gameLogMonitor = gameLogMonitor;
            _uiDispatcher = uiDispatcher;
            _toastQueueManager = toastQueueManager;
            _startupGate = startupGate;

            // 启动闸门：初值从 gate 读；后续 Complete 事件在 UI 线程翻转 IsStartupBusy（gate 可能在后台线程调 Complete）。
            _isStartupBusy = _startupGate.IsBusy;
            _startupGate.BusyChanged += OnStartupGateBusyChanged;

            // 在线更新启动事件：把主窗口切到"更新中锁死"模式。EventAggregator 默认 UI 线程订阅，安全。
            eventAggregator.GetEvent<OnlineUpdatingStartedEvent>()
                .Subscribe(() => IsOnlineUpdating = true, ThreadOption.UIThread);

            // Updater 中途退出（用户取消 / 下载失败）：把主 App 从"锁死"状态恢复到正常，让 App.OnStartup [4]
            // 继续走完 → 后续 [5] 登录 gate / [6] 主页导航。
            eventAggregator.GetEvent<OnlineUpdatingCancelledEvent>()
                .Subscribe(() =>
                {
                    IsOnlineUpdating = false;
                    // 释放 updateGate 让 [4] 里 await updateGate.WaitAsync() resume——它一直挂着等 Dismiss 或取消。
                    containerProvider.Resolve<IUpdateGateService>().Complete();
                }, ThreadOption.UIThread);

            _localization.PropertyChanged += OnLocalizationChanged;
            _navigation.Navigated += OnNavigated;
            _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;

            // 桥接 GameLogMonitor 事件 → GameStatus 状态
            _gameLogMonitor.BattleJoined += OnBattleJoined;
            _gameLogMonitor.BattleStarted += OnBattleStarted;
            _gameLogMonitor.BattleEnded += OnBattleEnded;

            // 启动游戏日志监控和状态监控
            _gameStatusMonitor.Start();
            _ = _gameLogMonitor.StartAsync();

            _updateService.UpdateAvailabilityChanged += OnUpdateAvailabilityChanged;
            IsUpdateAvailable = _updateService.IsUpdateAvailable;
            // 默认当前为最新版；若后续检查发现新版本，OnUpdateAvailabilityChanged 会切换 IsUpdateAvailable=true
            _updateCheckCompleted = true;

            ActivePage = PageNames.HomePage;

            UpdateCanNavigateToPersonal();
        }

        private void OnUpdateAvailabilityChanged(object? sender, UpdateAvailabilityChangedEventArgs args)
        {
            // fire-and-forget：UI 线程异步执行，避免在后台线程触发 PropertyChanged。
            _ = _uiDispatcher.InvokeAsync(() =>
            {
                _updateCheckCompleted = true;
                IsUpdateAvailable = args.IsAvailable;
                // 后台轮询命中新版：只点亮左下角"发现新版本"指示，不弹卡片打扰用户。
                // 启动检测 / 用户主动点击才弹更新卡片（后者由命令内显式导航，此处守卫防重）。
                if (args.Source == UpdateCheckSource.Background)
                    return;
                TryShowUpdateNotification(args.IsAvailable);
            });
        }

        /// <summary>
        /// 启动期发现新版本时弹出透明遮罩 + 白色卡片提示。
        /// 一次会话只弹一次，避免重复检查触发多次弹窗。
        /// </summary>
        private void TryShowUpdateNotification(bool isAvailable)
        {
            if (!isAvailable) return;
            if (_updateNotificationShown) return;
            _updateNotificationShown = true;

            try
            {
                EnsureModuleLoaded(PageNames.UpdateNotificationPage);
                _regionManager.RequestNavigate(
                    GlobalConstant.UpdateNotificationRegion,
                    PageNames.UpdateNotificationPage);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(MainWindowViewModel)}.{nameof(TryShowUpdateNotification)}", "弹出失败");
            }
        }


        /// <summary>
        /// Clean up event subscriptions. Called by MainWindow when closing.
        /// </summary>

        private void OnBattleJoined(object? sender, BattleEventArgs e)
        {
            _gameStatusMonitor.NotifyStatus(GameStatus.HeroSelection);
        }

        private void OnBattleStarted(object? sender, BattleEventArgs e)
        {
            _gameStatusMonitor.NotifyStatus(GameStatus.InGame);
        }

        private void OnBattleEnded(object? sender, BattleEventArgs e)
        {
            _gameStatusMonitor.NotifyStatus(GameStatus.BattleEnded);
        }

        /// <summary>
        /// 解除所有事件订阅。被 <see cref="Dispose(bool)"/> 自动调用，也可由 MainWindow 在关闭时显式调用
        /// 实现与窗口生命周期解耦的提前清理。
        /// </summary>
        public void Cleanup()
        {
            _localization.PropertyChanged -= OnLocalizationChanged;
            _navigation.Navigated -= OnNavigated;
            _gameStatusMonitor.GameStatusRecognized -= OnGameStatusRecognized;
            _gameLogMonitor.BattleJoined -= OnBattleJoined;
            _gameLogMonitor.BattleStarted -= OnBattleStarted;
            _gameLogMonitor.BattleEnded -= OnBattleEnded;
            _updateService.UpdateAvailabilityChanged -= OnUpdateAvailabilityChanged;
            _startupGate.BusyChanged -= OnStartupGateBusyChanged;
        }

        private void OnStartupGateBusyChanged(object? sender, EventArgs e)
        {
            // gate.Complete 可能在后台线程调；PropertyChanged 得到 UI 线程派发。
            _ = _uiDispatcher.InvokeAsync(() => IsStartupBusy = _startupGate.IsBusy);
        }

        /// <summary>
        /// 与 <see cref="ViewModelBase.Dispose(bool)"/> 联动：让基类的 Dispose 路径自动触发事件反订阅，
        /// 避免依赖 MainWindow 显式调用 <see cref="Cleanup"/>。重复 Dispose 由基类的 _disposed 标志守卫。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cleanup();
            }
            base.Dispose(disposing);
        }

        private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_localization.CurrentLanguage))
            {
                RaisePropertyChanged(nameof(CurrentVersionText));
                RefreshGameStatusText();
            }
        }

        private void OnGameStatusRecognized(object? sender, GameStatusChangedEventArgs args)
        {
            _currentGameStatus = args.Status;
            RefreshGameStatusText();
        }

        private void RefreshGameStatusText()
        {
            GameStatusText = _currentGameStatus switch
            {
                GameStatus.HeroSelection => _localizedText.Get("GameStatus.HeroSelection", "HeroSelection"),
                GameStatus.InGame => _localizedText.Get("GameStatus.InGame", "InGame"),
                GameStatus.BattleEnded => _localizedText.Get("GameStatus.BattleEnded", "BattleEnded"),
                _ => string.Empty,
            };
        }

        private void OnNavigated(string viewName)
        {
            ActivePage = viewName;
            RaisePropertyChanged(nameof(CanGoBack));
            UpdateCanNavigateToPersonal();
        }

        private void UpdateCanNavigateToPersonal()
        {
            var prefs = _playerPrefsService.Current;
            CanNavigateToPersonal = prefs.IsLoaded && !string.IsNullOrEmpty(prefs.PlayerName);
        }

        private void EnsureModuleLoaded(string viewName)
        {
            if (!viewName.EndsWith("Page"))
                return;

            var moduleName = viewName.Replace("Page", "Module");
            try
            {
                _moduleManager.LoadModule(moduleName);
            }
            catch
            {
            }
        }
    }
}

