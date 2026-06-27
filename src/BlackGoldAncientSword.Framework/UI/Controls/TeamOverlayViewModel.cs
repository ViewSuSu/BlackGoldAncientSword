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
                CloseRequested?.Invoke(this, EventArgs.Empty);
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

            // Step 1: 将当前成员按 UserName 建立索引（忽略大小写）
            var existingByName = new Dictionary<string, TeamOverlayMemberItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Members)
            {
                if (!string.IsNullOrEmpty(m.UserName))
                    existingByName[m.UserName] = m;
            }

            // Step 2: 收集需要移除的成员（UI 上不再出现的）
            var incomingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in members)
            {
                if (!string.IsNullOrEmpty(m.UserName))
                    incomingNames.Add(m.UserName);
            }

            for (int i = Members.Count - 1; i >= 0; i--)
            {
                var existing = Members[i];
                if (!string.IsNullOrEmpty(existing.UserName) && !incomingNames.Contains(existing.UserName))
                {
                    // 即将移除的 Image 绑定——清除 URL 让旧 BitmapImage 的引用释放
                    existing.AvatarUrl = string.Empty;
                    existing.RankIcon = string.Empty;
                    Members.RemoveAt(i);
                }
            }

            // Step 3: 更新或添加成员
            foreach (var m in members)
            {
                var userName = m.UserName ?? string.Empty;
                if (existingByName.TryGetValue(userName, out var existing))
                {
                    // 已有成员——原地更新属性，ItemsControl 不会重建容器
                    existing.AvatarUrl = m.AvatarUrl;
                    existing.RankName = m.RankName;
                    existing.RankIcon = m.RankIcon;
                    existing.PageRankName = m.PageRankName;
                    existing.PageStarCount = m.PageStarCount;
                    existing.PageHasStars = m.PageHasStars;
                    existing.RankTierScore = m.RankTierScore;
                    existing.IsLoading = m.IsLoading;
                }
                else
                {
                    // 新成员——添加到集合
                    Members.Add(new TeamOverlayMemberItem
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
