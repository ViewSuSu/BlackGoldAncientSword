using System.Windows;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.ClosePrompt.ViewModels
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
            _minimizeToTaskbarCommand ??= new DelegateCommand(async () =>
            {
                _settingsService.Current.CloseBehavior = "MinimizeToTaskbar";
                _settingsService.Current.CloseBehaviorRemembered = RememberChoice;
                if (RememberChoice)
                    await _settingsService.SaveAsync();
                DismissOverlay();
                Application.Current.MainWindow!.Close();
            });


        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                DismissOverlay();
            });

        private DelegateCommand? _exitDirectlyCommand;
        public DelegateCommand ExitDirectlyCommand =>
            _exitDirectlyCommand ??= new DelegateCommand(async () =>
            {
                _settingsService.Current.CloseBehavior = "ExitDirectly";
                _settingsService.Current.CloseBehaviorRemembered = RememberChoice;
                if (RememberChoice)
                    await _settingsService.SaveAsync();
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
