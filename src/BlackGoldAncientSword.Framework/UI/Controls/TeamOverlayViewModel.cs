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
        public DelegateCommand RefreshCommand { get; }

        public event EventHandler? CloseRequested;
        public event EventHandler? NavigateToTeamInfoRequested;
        public event EventHandler<bool>? DontShowAgainChanged;
        public event EventHandler? RefreshRequested;

        public TeamOverlayViewModel()
        {
            CloseCommand = new DelegateCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            RefreshCommand = new DelegateCommand(() => RefreshRequested?.Invoke(this, EventArgs.Empty));
            NavigateToTeamInfoCommand = new DelegateCommand(() =>
            {
                NavigateToTeamInfoRequested?.Invoke(this, EventArgs.Empty);
                var navigation = containerProvider.Resolve<IMainContentNavigationService>();
                navigation.NavigateTo(PageNames.TeamInfoPage);
            });
        }

        /// <summary>
        /// 更新队伍成员列表。与旧实现不同，本方法对已存在的成员（按 UserName 匹配）
        /// 原地更新属性而非清空重建，避免 WPF ItemsControl 每次重建可视化树触发 Image 控件
        /// 创建新的 BitmapImage，从而减少 WPF 非托管 MIL 内存积累。
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

            var incomingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in members)
            {
                if (!string.IsNullOrEmpty(m.UserName))
                    incomingNames.Add(m.UserName);
            }

            // 移除不在新名单中的成员
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                var existing = Members[i];
                if (!string.IsNullOrEmpty(existing.UserName) && !incomingNames.Contains(existing.UserName))
                {
                    existing.AvatarUrl = string.Empty;
                    existing.RankIcon = string.Empty;
                    Members.RemoveAt(i);
                }
            }

            // 建立现有成员索引（移除后进行，保证只包含保留的成员）
            var existingByName = new Dictionary<string, TeamOverlayMemberItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Members)
            {
                if (!string.IsNullOrEmpty(m.UserName))
                    existingByName[m.UserName] = m;
            }

            // 按传入顺序重建集合，复用已有成员对象以保持 WPF 可视化树稳定
            for (int targetIdx = 0; targetIdx < members.Count; targetIdx++)
            {
                var m = members[targetIdx];
                var userName = m.UserName ?? string.Empty;
                if (existingByName.TryGetValue(userName, out var existing))
                {
                    existing.AvatarUrl = m.AvatarUrl;
                    existing.RankName = m.RankName;
                    existing.RankIcon = m.RankIcon;
                    existing.PageRankName = m.PageRankName;
                    existing.PageStarCount = m.PageStarCount;
                    existing.PageHasStars = m.PageHasStars;
                    existing.RankTierScore = m.RankTierScore;
                    existing.IsLoading = m.IsLoading;

                    var currentIdx = Members.IndexOf(existing);
                    if (currentIdx >= 0 && currentIdx != targetIdx)
                        Members.Move(currentIdx, targetIdx);
                }
                else
                {
                    Members.Insert(targetIdx, new TeamOverlayMemberItem
                    {
                        UserName = userName,
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
            }

            RaisePropertyChanged(nameof(HasMembers));
        }

        /// <summary>
        /// 清除所有成员的图片绑定 URL，释放 BitmapImage 引用，让 WPF 非托管解码内存可被回收。
        /// </summary>
        public void ClearImageBindings()
        {
            foreach (var m in Members)
            {
                m.AvatarUrl = string.Empty;
                m.RankIcon = string.Empty;
            }
        }
    }
}
