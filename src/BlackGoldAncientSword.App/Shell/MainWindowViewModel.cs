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

        private DelegateCommand? _navigateToAnnouncementCommand;

        public string CurrentVersionText =>
            string.Format(
                _localizedText.Get("Settings.CurrentVersion", "{0}"),
                _updateService.CurrentVersion);

        private bool _updateCheckCompleted;
        private bool _updateNotificationShown;
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
            _checkForUpdatesCommand ??= new DelegateCommand(() =>
            {
                // 左下角"发现新版本"点击 → 复用启动期发现新版时的半透明卡片提示。
                // 用户主动点击：忽略 _updateNotificationShown 守卫，以便关闭后还能再次打开。
                try
                {
                    EnsureModuleLoaded(PageNames.UpdateNotificationPage);
                    _regionManager.RequestNavigate(
                        GlobalConstant.UpdateNotificationRegion,
                        PageNames.UpdateNotificationPage);
                    _updateNotificationShown = true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(MainWindowViewModel)}.{nameof(CheckForUpdatesCommand)}", "弹出更新通知失败");
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

        private void OnUpdateAvailabilityChanged(object? sender, bool isAvailable)
        {
            // fire-and-forget：UI 线程异步执行，避免在后台线程触发 PropertyChanged。
            _ = _uiDispatcher.InvokeAsync(() =>
            {
                _updateCheckCompleted = true;
                IsUpdateAvailable = isAvailable;
                TryShowUpdateNotification(isAvailable);
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

