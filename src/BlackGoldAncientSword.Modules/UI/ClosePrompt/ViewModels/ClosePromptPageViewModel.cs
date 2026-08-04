using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.ClosePrompt.ViewModels
{
    public class ClosePromptPageViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IApplicationLifetime _appLifetime;
        private readonly IUIDispatcher _uiDispatcher;

        private bool _rememberChoice;
        public bool RememberChoice
        {
            get => _rememberChoice;
            set
            {
                if (_rememberChoice == value) return;
                _rememberChoice = value;
                RaisePropertyChanged(nameof(RememberChoice));

                // 勾选变化即持久化：写入内存 + SaveAsync，SaveAsync 完成后 SettingsService 广播
                // SettingsChanged，已打开的设置页 ViewModel 同步刷新 RememberCloseBehavior 绑定。
                // 落盘与 UI 保持严格实时同步——用户要求"落盘数据跟 UI 实时同步"。
                _settingsService.Current.CloseBehaviorRemembered = value;
                _settingsService.SaveAsync().SafeFireAndForget(
                    $"{nameof(ClosePromptPageViewModel)}.{nameof(RememberChoice)}.SaveAsync");
            }
        }

        public ClosePromptPageViewModel(
            ISettingsService settingsService,
            IApplicationLifetime appLifetime,
            IUIDispatcher uiDispatcher)
        {
            _settingsService = settingsService;
            _appLifetime = appLifetime;
            _uiDispatcher = uiDispatcher;

            // 从持久化值初始化，避免每次弹出对话框都重置为 false
            _rememberChoice = _settingsService.Current.CloseBehaviorRemembered;

            // 订阅外部改动（例如设置页勾选/托盘菜单切换）：对话框浮层与设置页同屏可见时保持双向一致
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            if (_isDisposed) return;
            if (_uiDispatcher.CheckAccess())
                ApplyRememberFromSettings();
            else
                _uiDispatcher.BeginInvoke(ApplyRememberFromSettings);
        }

        private void ApplyRememberFromSettings()
        {
            var value = _settingsService.Current.CloseBehaviorRemembered;
            if (_rememberChoice == value) return;
            // 直接写底字段，绕过 setter 中的 SaveAsync 分支，防止外部广播 → 本地更新 → 再次 SaveAsync 的回响循环
            _rememberChoice = value;
            RaisePropertyChanged(nameof(RememberChoice));
        }

        private bool _isDisposed;
        protected override void Dispose(bool disposing)
        {
            if (_isDisposed) { base.Dispose(disposing); return; }
            _isDisposed = true;
            if (disposing)
            {
                _settingsService.SettingsChanged -= OnSettingsChanged;
            }
            base.Dispose(disposing);
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
                    AppLog.Error(ex, $"{nameof(ClosePromptPageViewModel)}.{nameof(MinimizeToTaskbarCommand)}");
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
                    AppLog.Error(ex, $"{nameof(ClosePromptPageViewModel)}.{nameof(ExitDirectlyCommand)}");
                }
            });

        private void DismissOverlay()
        {
            var region = regionManager.Regions[GlobalConstant.ClosePromptRegion];
            region.RemoveAll();
        }
    }
}
