using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        private string _countdownText = string.Empty;
        public string CountdownText
        {
            get => _countdownText;
            set
            {
                if (_countdownText == value) return;
                _countdownText = value;
                RaisePropertyChanged(nameof(CountdownText));
            }
        }

        public DelegateCommand CloseCommand { get; }
        public DelegateCommand NavigateToTeamInfoCommand { get; }

        public event EventHandler? CloseRequested;
        public event EventHandler? NavigateToTeamInfoRequested;
        public event EventHandler<bool>? DontShowAgainChanged;

        public TeamOverlayViewModel()
        {
            CloseCommand = new DelegateCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            NavigateToTeamInfoCommand = new DelegateCommand(() =>
            {
                NavigateToTeamInfoRequested?.Invoke(this, EventArgs.Empty);
                var navigation = containerProvider.Resolve<IMainContentNavigationService>();
                navigation.NavigateTo(PageNames.TeamInfoPage);
            });
        }

        /// <summary>
        /// 用下发的完整队员名单刷新集合：成员数量与顺序一律以传入列表为准。
        /// <para>
        /// 传入列表是本局队伍的完整快照，因此按下标复用已有成员对象原位更新属性
        /// （而非清空重建），既保持顺序正确，也让 WPF 可视化树稳定，
        /// 避免 ItemsControl 每次重建时 Image 控件创建新的 BitmapImage 积累非托管 MIL 内存。
        /// </para>
        /// <para>
        /// 不能按 UserName 匹配已有成员：成员昵称不保证已有（可能始终为空），空名字的成员
        /// 既匹配不上旧项（每次刷新都被当成新成员插入）、又不会被移除，弹窗会越刷越多空位。
        /// </para>
        /// </summary>
        public void UpdateMembers(IList<TeamOverlayMemberItem> members)
        {
            if (members == null || members.Count == 0)
            {
                if (Members.Count > 0)
                {
                    ClearImageBindings();
                    Members.Clear();
                }
                RaisePropertyChanged(nameof(HasMembers));
                return;
            }

            // 超出的尾部项已不在本局名单中（队友退出/换人后名单变短）：直接移除。
            for (int i = Members.Count - 1; i >= members.Count; i--)
            {
                ReleaseImageBindings(Members[i]);
                Members.RemoveAt(i);
            }

            for (int i = 0; i < members.Count; i++)
            {
                var incoming = members[i];
                if (i < Members.Count)
                    CopyMember(Members[i], incoming);
                else
                    Members.Add(CopyOf(incoming));
            }

            RaisePropertyChanged(nameof(HasMembers));
        }

        /// <summary>
        /// 清除所有成员的图片绑定 URL，释放 BitmapImage 引用，让 WPF 非托管解码内存可被回收。
        /// </summary>
        public void ClearImageBindings()
        {
            foreach (var m in Members)
                ReleaseImageBindings(m);
        }

        private static void ReleaseImageBindings(TeamOverlayMemberItem member)
        {
            member.AvatarUrl = string.Empty;
            member.RankIcon = string.Empty;
        }

        private static void CopyMember(TeamOverlayMemberItem target, TeamOverlayMemberItem source)
        {
            target.UserName = source.UserName;
            target.AvatarUrl = source.AvatarUrl;
            target.RankName = source.RankName;
            target.RankIcon = source.RankIcon;
            target.PageRankName = source.PageRankName;
            target.PageStarCount = source.PageStarCount;
            target.PageHasStars = source.PageHasStars;
            target.IsLoading = source.IsLoading;
        }

        private static TeamOverlayMemberItem CopyOf(TeamOverlayMemberItem source)
        {
            var copy = new TeamOverlayMemberItem();
            CopyMember(copy, source);
            return copy;
        }
    }
}
