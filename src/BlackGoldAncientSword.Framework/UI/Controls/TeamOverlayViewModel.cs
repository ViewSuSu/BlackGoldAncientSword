using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    public class TeamOverlayViewModel : ViewModelBase
    {
        public ObservableCollection<TeamOverlayMemberItem> Members { get; } = new();

        private bool _dontShowAgain;
        public bool DontShowAgain
        {
            get => _dontShowAgain;
            set
            {
                if (_dontShowAgain == value) return;
                _dontShowAgain = value;
                RaisePropertyChanged(nameof(DontShowAgain));
                DontShowAgainChanged?.Invoke(this, value);
            }
        }

        public bool HasMembers => Members.Count > 0;

        public DelegateCommand CloseCommand { get; }
        public DelegateCommand NavigateToTeamInfoCommand { get; }
        public DelegateCommand RefreshCommand { get; }

        public event EventHandler? CloseRequested;
        public event EventHandler<bool>? DontShowAgainChanged;
        public event EventHandler? RefreshRequested;

        public TeamOverlayViewModel()
        {
            CloseCommand = new DelegateCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            RefreshCommand = new DelegateCommand(() => RefreshRequested?.Invoke(this, EventArgs.Empty));
            NavigateToTeamInfoCommand = new DelegateCommand(() =>
            {
                var navigation = containerProvider.Resolve<IMainContentNavigationService>();
                navigation.NavigateTo(PageNames.TeamInfoPage);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        public void UpdateMembers(IList<TeamOverlayMemberItem> members)
        {
            Members.Clear();
            if (members == null) return;

            foreach (var m in members)
            {
                Members.Add(new TeamOverlayMemberItem
                {
                    UserName = m.UserName,
                    AvatarUrl = m.AvatarUrl,
                    RankName = m.RankName,
                    RankIcon = m.RankIcon,
                    PageRankName = m.PageRankName,
                    PageStarCount = m.PageStarCount,
                    PageHasStars = m.PageHasStars,
                    RankTierScore = m.RankTierScore,
                    IsLoading = m.IsLoading
                });
            }

            RaisePropertyChanged(nameof(HasMembers));
        }
    }
}
