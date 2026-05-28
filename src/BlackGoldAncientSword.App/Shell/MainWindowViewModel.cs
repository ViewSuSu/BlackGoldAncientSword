using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
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
        private readonly IGameStatusMonitor _gameStatusMonitor;

        public ObservableCollection<ToastItem> ToastItems { get; } = new();

        private string _activePage = string.Empty;
        public string ActivePage
        {
            get => _activePage;
            set => SetProperty(ref _activePage, value);
        }

        private GameStatus _currentGameStatus = GameStatus.Unknown;
        private string _gameStatusText = string.Empty;
        public string GameStatusText
        {
            get => _gameStatusText;
            set => SetProperty(ref _gameStatusText, value);
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/ViewSuSu/BlackGoldAncientSword/issues/new",
                    UseShellExecute = true
                });
            });

        private DelegateCommand? _navigateToAnnouncementCommand;

        public string CurrentVersionText =>
            string.Format(
                System.Windows.Application.Current?.TryFindResource("Settings.CurrentVersion") as string ?? "{0}",
                _updateService.CurrentVersion);

        public bool CanGoBack => _navigation.CanGoBack;
        private bool _canNavigateToPersonal;
        public bool CanNavigateToPersonal
        {
            get => _canNavigateToPersonal;
            set => SetProperty(ref _canNavigateToPersonal, value);
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
                await _updateService.CheckForUpdatesAsync();
            });

        public MainWindowViewModel(
            IPlayerPrefsService playerPrefsService,
            IMainContentNavigationService navigation,
            IRegionManager regionManager,
            IModuleManager moduleManager,
            BlackGoldAncientSword.Framework.Services.Abstractions.IUpdateService updateService,
            BlackGoldAncientSword.Framework.Services.Abstractions.ILocalizationService localizationService,
            IGameStatusMonitor gameStatusMonitor)
        {
            _playerPrefsService = playerPrefsService;
            _navigation = navigation;
            _regionManager = regionManager;
            _moduleManager = moduleManager;
            _updateService = updateService;
            _localization = localizationService;
            _gameStatusMonitor = gameStatusMonitor;

            _localization.PropertyChanged += OnLocalizationChanged;
            _navigation.Navigated += OnNavigated;
            _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;

            ActivePage = PageNames.HomePage;

            UpdateCanNavigateToPersonal();
            eventAggregator.GetEvent<TipMessageEvent>()
                .Subscribe(args =>
                {
                    var item = new ToastItem
                    {
                        Message = args.Message,
                        IsError = args.HighlightTexts.Contains("Error")
                    };
                    ToastItems.Add(item);
                }, ThreadOption.UIThread);
        }

        /// <summary>
        /// Clean up event subscriptions. Called by MainWindow when closing.
        /// </summary>
        public void Cleanup()
        {
            _localization.PropertyChanged -= OnLocalizationChanged;
            _navigation.Navigated -= OnNavigated;
            _gameStatusMonitor.GameStatusRecognized -= OnGameStatusRecognized;
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
                GameStatus.HeroSelection => System.Windows.Application.Current.TryFindResource("GameStatus.HeroSelection") as string ?? "HeroSelection",
                GameStatus.InGame => System.Windows.Application.Current.TryFindResource("GameStatus.InGame") as string ?? "InGame",
                GameStatus.BattleEnded => System.Windows.Application.Current.TryFindResource("GameStatus.BattleEnded") as string ?? "BattleEnded",
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

    public class ToastItem : ViewModelBase
    {
        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set => SetProperty(ref _isError, value);
        }
    }
}
