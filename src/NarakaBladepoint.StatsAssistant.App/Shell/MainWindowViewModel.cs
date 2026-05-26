using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Diagnostics;
using NarakaBladepoint.StatsAssistant.Framework.Core.Bases.ViewModels;
using NarakaBladepoint.StatsAssistant.Framework.Core.Events;
using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;
using NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure;
using Prism.Regions;

namespace NarakaBladepoint.StatsAssistant.App.Shell
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IMainContentNavigationService _navigation;
        private readonly IRegionManager _regionManager;
        private readonly IModuleManager _moduleManager;

        public ObservableCollection<ToastItem> ToastItems { get; } = new();

        private string _activePage = string.Empty;
        public string ActivePage
        {
            get => _activePage;
            set => SetProperty(ref _activePage, value);
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
                    FileName = "https://github.com/ViewSuSu/NarakaBladepoint-Stats-Assistant/issues/new",
                    UseShellExecute = true
                });
            });

        private DelegateCommand? _navigateToAnnouncementCommand;
        public bool CanGoBack => _navigation.CanGoBack;

        private DelegateCommand? _goBackCommand;
        public DelegateCommand GoBackCommand =>
            _goBackCommand ??= new DelegateCommand(() =>
            {
                _navigation.GoBack();
            }).ObservesCanExecute(() => CanGoBack);
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

        public MainWindowViewModel(
            IMainContentNavigationService navigation,
            IRegionManager regionManager,
            IModuleManager moduleManager)
        {
            _navigation = navigation;
            _regionManager = regionManager;
            _moduleManager = moduleManager;

            _navigation.Navigated += OnNavigated;
            ActivePage = PageNames.HomePage;

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

        private void OnNavigated(string viewName)
        {
            ActivePage = viewName;
            RaisePropertyChanged(nameof(CanGoBack));
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
                // Module may already be loaded or doesn't exist as OnDemand
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
