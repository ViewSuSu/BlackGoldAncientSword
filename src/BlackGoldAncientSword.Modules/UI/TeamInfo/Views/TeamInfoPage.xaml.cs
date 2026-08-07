using System.ComponentModel;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Bases.Views;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Views
{
    public partial class TeamInfoPage : UserControlBase
    {
        public TeamInfoPage()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TeamInfoPageViewModel oldVm)
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is TeamInfoPageViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                SyncColumnWidths(newVm);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // ColXWidth (GridLength) 属性已从 VM 中移除以解除对 System.Windows 的硬耦合。
            // 列宽 = 简单 bool 表达：HasMember0/HasMember1/HasMember2/HasDiffLeft/HasDiffRight。
            // View code-behind 监听这 5 个 bool 的变化即可重新构造 ColumnDefinitions[].Width。
            if (sender is TeamInfoPageViewModel vm)
            {
                switch (e.PropertyName)
                {
                    case nameof(TeamInfoPageViewModel.HasMember0):
                    case nameof(TeamInfoPageViewModel.HasMember1):
                    case nameof(TeamInfoPageViewModel.HasMember2):
                    case nameof(TeamInfoPageViewModel.HasDiffLeft):
                    case nameof(TeamInfoPageViewModel.HasDiffRight):
                        SyncColumnWidths(vm);
                        break;
                }
            }
        }

        private void SyncColumnWidths(TeamInfoPageViewModel vm)
        {
            // 5 列结构：卡 | diff列 | 卡 | diff列 | 卡。
            // 卡片列 3*、diff 列 1*：diff 列随窗口变宽同步增长（数据展示不全时可拉伸窗口），
            // 隐藏时收缩为 0。
            if (MainContentGrid.ColumnDefinitions.Count < 5) return;
            MainContentGrid.ColumnDefinitions[0].Width = vm.HasMember0 ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
            MainContentGrid.ColumnDefinitions[1].Width = vm.HasDiffLeft ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            MainContentGrid.ColumnDefinitions[2].Width = vm.HasMember1 ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
            MainContentGrid.ColumnDefinitions[3].Width = vm.HasDiffRight ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            MainContentGrid.ColumnDefinitions[4].Width = vm.HasMember2 ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        }
    }
}
