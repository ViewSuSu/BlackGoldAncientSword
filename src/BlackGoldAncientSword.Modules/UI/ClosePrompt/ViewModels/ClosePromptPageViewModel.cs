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
                    // 直接最小化，不走 MainWindow.Close()；否则 OnClosing 在未勾选"记住选项"时
                    // 会再次发现 CloseBehaviorRemembered==false 而重新弹出本提示，造成无限循环。
                    _appLifetime.MinimizeMainWindow();
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
                    // 走强制终止而非 Application.Shutdown：后者会触发 MainWindow.OnClosing，
                    // 未勾选"记住选项"时又会重弹本提示。强制终止与 CloseButton 兜底路径一致，
                    // 由 JobObject 负责清理子进程。
                    _appLifetime.ForceTerminate();
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
