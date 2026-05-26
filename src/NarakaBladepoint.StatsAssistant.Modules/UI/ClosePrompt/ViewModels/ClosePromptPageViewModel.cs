using System.Windows;
using NarakaBladepoint.StatsAssistant.Framework.Core.Bases.ViewModels;
using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Modules.UI.ClosePrompt.ViewModels
{
    public class ClosePromptPageViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;

        private bool _rememberChoice;
        public bool RememberChoice
        {
            get => _rememberChoice;
            set => SetProperty(ref _rememberChoice, value);
        }

        public ClosePromptPageViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        private DelegateCommand? _minimizeToTaskbarCommand;
        public DelegateCommand MinimizeToTaskbarCommand =>
            _minimizeToTaskbarCommand ??= new DelegateCommand(() =>
            {
                if (RememberChoice)
                {
                    _settingsService.Current.CloseBehavior = "MinimizeToTaskbar";
                    _settingsService.Current.CloseBehaviorRemembered = true;
                    _ = System.Threading.Tasks.Task.Run(() => _settingsService.SaveAsync());
                }
                DismissOverlay();
                Application.Current.MainWindow!.WindowState = WindowState.Minimized;
            });

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                DismissOverlay();
            });

        private DelegateCommand? _exitDirectlyCommand;
        public DelegateCommand ExitDirectlyCommand =>
            _exitDirectlyCommand ??= new DelegateCommand(() =>
            {
                if (RememberChoice)
                {
                    _settingsService.Current.CloseBehavior = "ExitDirectly";
                    _settingsService.Current.CloseBehaviorRemembered = true;
                    _ = System.Threading.Tasks.Task.Run(() => _settingsService.SaveAsync());
                }
                DismissOverlay();
                Application.Current.Shutdown();
            });

        private void DismissOverlay()
        {
            var region = regionManager.Regions[GlobalConstant.ClosePromptRegion];
            region.RemoveAll();
        }
    }
}