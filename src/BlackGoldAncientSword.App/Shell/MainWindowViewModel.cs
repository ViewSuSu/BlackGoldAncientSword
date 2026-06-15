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
        private readonly DateTime _startupTime = DateTime.UtcNow;
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
                // async DelegateCommand 实际是 async void：整段必须用 try/catch 兜底，
                // 否则任何异常都会冒泡到 DispatcherUnhandledException。
                try
                {
                    Debug.WriteLine($"[{nameof(MainWindowViewModel)}.{nameof(CheckForUpdatesCommand)}] 执行：打开 GitHub 下载最新版本");
                    string url;
                    try
                    {
                        // Fetch latest release download URL from GitHub API
                        using var http = new System.Net.Http.HttpClient();
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");
                        var json = await http.GetStringAsync("https://api.github.com/repos/ViewSuSu/BlackGoldAncientSword/releases/latest").ConfigureAwait(false);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var assets = doc.RootElement.GetProperty("assets");
                        string? downloadUrl = null;
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString();
                            if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                break;
                            }
                        }
                        url = downloadUrl ?? "https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest";
                    }
                    catch (Exception apiEx) when (apiEx is not OutOfMemoryException and not StackOverflowException)
                    {
                        Debug.WriteLine($"[{nameof(MainWindowViewModel)}.{nameof(CheckForUpdatesCommand)}] GitHub API 查询失败，回退到 releases 页面: {apiEx.Message}");
                        url = "https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest";
                    }
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    Debug.WriteLine($"[{nameof(MainWindowViewModel)}.{nameof(CheckForUpdatesCommand)}] 完成");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.WriteLine($"[{nameof(MainWindowViewModel)}.{nameof(CheckForUpdatesCommand)}] 启动下载页失败: {ex}");
                }
            });

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
            ToastQueueManager toastQueueManager)
        {
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
            });
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

            // 英雄选择阶段自动导航到队伍信息页，确保 OCR 识别启动，
            // 无论用户当前在哪个页面。
            // 如果已经在 TeamInfoPage，MainContentNavigator 会自动跳过重复导航。
            // 程序启动后延迟 3 秒才启用自动导航，避免启动时游戏已在英雄选择中
            // 立刻跳转到队伍信息页，用户看不到启动页。
            // 如果用户后来才启动游戏进入英雄选择，3 秒早已过去，导航正常触发。
            if (args.Status == GameStatus.HeroSelection &&
                (DateTime.UtcNow - _startupTime).TotalSeconds > 3)
            {
                _navigation.NavigateTo(PageNames.TeamInfoPage);
            }
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
