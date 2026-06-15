using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.ClosePrompt.ViewModels
{
    public class ClosePromptPageViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IApplicationLifetime _appLifetime;

        private bool _rememberChoice;
        public bool RememberChoice
        {
            get => _rememberChoice;
            set
            {
                if (_rememberChoice == value) return;
                _rememberChoice = value;
                RaisePropertyChanged(nameof(RememberChoice));
            }
        }

        public ClosePromptPageViewModel(
            ISettingsService settingsService,
            IApplicationLifetime appLifetime)
        {
            _settingsService = settingsService;
            _appLifetime = appLifetime;
        }

        private DelegateCommand? _minimizeToTaskbarCommand;
        public DelegateCommand MinimizeToTaskbarCommand =>
            _minimizeToTaskbarCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    _settingsService.Current.CloseBehavior = "MinimizeToTaskbar";
                    _settingsService.Current.CloseBehaviorRemembered = RememberChoice;
                    if (RememberChoice)
                        await _settingsService.SaveAsync();
                    DismissOverlay();
                    _appLifetime.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(ClosePromptPageViewModel)}] {nameof(MinimizeToTaskbarCommand)} failed: {ex}");
                }
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
                try
                {
                    _settingsService.Current.CloseBehavior = "ExitDirectly";
                    _settingsService.Current.CloseBehaviorRemembered = RememberChoice;
                    if (RememberChoice)
                        await _settingsService.SaveAsync();
                    DismissOverlay();
                    _appLifetime.Shutdown();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(ClosePromptPageViewModel)}] {nameof(ExitDirectlyCommand)} failed: {ex}");
                }
            });

        private void DismissOverlay()
        {
            var region = regionManager.Regions[GlobalConstant.ClosePromptRegion];
            region.RemoveAll();
        }
    }
}
