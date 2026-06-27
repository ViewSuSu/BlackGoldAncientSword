using System;
using System.Collections.Generic;
using System.Windows.Threading;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.UI.Controls;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class TeamOverlayService : ITeamOverlayService
    {
        private const int CountdownStartSeconds = 30;

        private TeamOverlayWindow? _window;
        private TeamOverlayViewModel? _viewModel;
        private DispatcherTimer? _countdownTimer;
        private int _countdownRemaining;
        private readonly ISettingsService _settingsService;

        public TeamOverlayService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public Action? RefreshAction { get; set; }
        public event Action? Dismissed;
        public event Action? NavigateToTeamInfoRequested;

        public void Show(IList<TeamOverlayMemberItem> members)
        {
            if (!_settingsService.Current.ShowTeamOverlayDuringHeroSelection) return;

            EnsureWindowCreated();
            _viewModel!.DontShowAgain = false;
            _viewModel.UpdateMembers(members);
            _window!.Show();
            _window.PositionOnGameMonitor();
            StartCountdown();
        }

        public void Hide()
        {
            if (_window != null)
            {
                StopCountdown();
                // 清除图片绑定 URL，释放对 BitmapImage 的引用
                // 让 WPF 非托管 MIL 解码内存可被回收
                _viewModel?.ClearImageBindings();
                _window.Hide();
            }
        }

        private void StartCountdown()
        {
            _countdownRemaining = CountdownStartSeconds;
            _viewModel!.CountdownText = $"{_countdownRemaining}s";

            if (_countdownTimer == null)
            {
                _countdownTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _countdownTimer.Tick += OnCountdownTick;
            }
            _countdownTimer.Start();
        }

        private void StopCountdown()
        {
            _countdownTimer?.Stop();
            if (_viewModel != null)
                _viewModel.CountdownText = string.Empty;
        }

        private void OnCountdownTick(object? sender, EventArgs e)
        {
            _countdownRemaining--;
            if (_countdownRemaining <= 0)
            {
                StopCountdown();
                _viewModel?.ClearImageBindings();
                _window?.Hide();
                Dismissed?.Invoke();
                return;
            }
            if (_viewModel != null)
                _viewModel.CountdownText = $"{_countdownRemaining}s";
        }

        private void EnsureWindowCreated()
        {
            if (_window != null) return;

            _viewModel = new TeamOverlayViewModel();
            _viewModel.DontShowAgainChanged += OnDontShowAgainChanged;
            _viewModel.RefreshRequested += OnRefreshRequested;
            _viewModel.CloseRequested += OnOverlayDismissed;
            _viewModel.NavigateToTeamInfoRequested += OnNavigateToTeamInfoRequested;
            _window = new TeamOverlayWindow(_viewModel);
        }

        private void OnDontShowAgainChanged(object? sender, bool dontShow)
        {
            if (dontShow)
            {
                _settingsService.Current.ShowTeamOverlayDuringHeroSelection = false;
                _settingsService.SaveAsync().SafeFireAndForget("TeamOverlayService.SaveDismissed");
            }
        }

        private void OnRefreshRequested(object? sender, EventArgs e)
        {
            RefreshAction?.Invoke();
        }

        private void OnOverlayDismissed(object? sender, EventArgs e)
        {
            Dismissed?.Invoke();
        }

        private void OnNavigateToTeamInfoRequested(object? sender, EventArgs e)
        {
            NavigateToTeamInfoRequested?.Invoke();
        }
    }
}
