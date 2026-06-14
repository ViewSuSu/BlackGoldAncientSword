using System;
using System.Collections.Generic;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.UI.Controls;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class TeamOverlayService : ITeamOverlayService
    {
        private TeamOverlayWindow? _window;
        private TeamOverlayViewModel? _viewModel;
        private readonly ISettingsService _settingsService;

        public TeamOverlayService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public Action? RefreshAction { get; set; }
        public event Action? Dismissed;

        public void Show(IList<TeamOverlayMemberItem> members)
        {
            if (!_settingsService.Current.ShowTeamOverlayDuringHeroSelection) return;

            EnsureWindowCreated();
            _viewModel!.DontShowAgain = false;
            _viewModel.UpdateMembers(members);
            _window!.Show();
            _window.PositionOnGameMonitor();
        }

        public void Hide()
        {
            if (_window != null)
                _window.Hide();
        }

        private void EnsureWindowCreated()
        {
            if (_window != null) return;

            _viewModel = new TeamOverlayViewModel();
            _viewModel.DontShowAgainChanged += OnDontShowAgainChanged;
            _viewModel.RefreshRequested += OnRefreshRequested;
            _viewModel.CloseRequested += OnOverlayDismissed;
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
    }
}
