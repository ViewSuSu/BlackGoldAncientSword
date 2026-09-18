using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BlackGoldAncientSword.Modules.UI.Stats.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.Stats.Views
{
    public partial class StatsPage : UserControlBase
    {
        public StatsPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 让对局列表"滚不动时把滚轮交还给外层"。
        /// 本页整页套在页面级 ScrollViewer 里，而 DataGrid 的模板自带一层 ScrollViewer（连列头都
        /// 在它内部）。WPF 的 ScrollViewer.OnMouseWheel 一旦拿到事件就**无条件**置 Handled——哪怕
        /// 它自己一像素都滚不动：列表被外层撑成完整高度时正是如此。滚轮于是断在列表上、冒泡不到
        /// 外层，表现就是鼠标落在对局记录上时整页滚不动。
        /// 内层还能滚就让它自己滚（将来列表若改成固定高度仍然成立），滚不动才把事件转给外层。
        /// </summary>
        private void OnBattleGridPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not DataGrid grid) return;
            if (FindInnerScrollViewer(grid) is { } inner && CanScroll(inner, e.Delta)) return;
            if (FindParentScrollViewer(grid) is not { } outer) return;

            // 外层只认落在它自己路由上的 MouseWheel：重新发一个，而不是去改它的偏移量，
            // 这样平滑滚动 / CanContentScroll 等设置都还由外层 ScrollViewer 自己决定。
            e.Handled = true;
            outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = grid,
            });
        }

        /// <summary>该 ScrollViewer 还能不能朝滚轮方向继续滚（已经在端点就是滚不动了）。</summary>
        private static bool CanScroll(ScrollViewer viewer, int delta)
            => delta > 0
                ? viewer.VerticalOffset > 0
                : viewer.VerticalOffset < viewer.ScrollableHeight;

        /// <summary>DataGrid 模板里的内层 ScrollViewer（列表自身的滚动宿主）。</summary>
        private static ScrollViewer? FindInnerScrollViewer(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer viewer) return viewer;
                if (FindInnerScrollViewer(child) is { } nested) return nested;
            }
            return null;
        }

        /// <summary>包住本页的页面级 ScrollViewer。</summary>
        private static ScrollViewer? FindParentScrollViewer(DependencyObject node)
        {
            for (var parent = VisualTreeHelper.GetParent(node); parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is ScrollViewer viewer) return viewer;
            }
            return null;
        }

        private void OnRankBlockClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { DataContext: RecentRankDisplayItem block }) return;
            if (DataContext is not StatsPageViewModel vm) return;

            // 这里直接同步取返回值，不要改成"VM 置属性 + View 订阅 PropertyChanged"的写法：
            // 本页继承 UserControlBase，DataContext 由基类构造函数里的 Prism 自动装配**同步**赋好，
            // 那一刻本类构造函数体还没执行，在这里订阅 DataContextChanged 永远收不到，通知路径会
            // 静默失效——表现就是点了完全没反应。滚动本身也只能在 View 做（ScrollIntoView 是
            // DataGrid 的 API），所以由 View 主动问 VM "该定位到哪一行"，而不是等 VM 通知。
            if (vm.ResolveBattleForRankBlock(block) is not { } target) return;

            BattleGrid.ScrollIntoView(target);
            BattleGrid.SelectedItem = target;
        }

        /// <summary>
        /// 输入框里的上下键用来在候选列表里移动高亮（焦点始终留在输入框，可以一边打字一边选），
        /// Esc 收起候选。回车不在这里拦——它由 SearchBar 自己的搜索命令触发，走 VM 的确认逻辑。
        /// </summary>
        private void OnSearchInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not StatsPageViewModel vm) return;

            switch (e.Key)
            {
                case Key.Down:
                    vm.MoveSuggestionSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MoveSuggestionSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.CloseSuggestions();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// 候选列表滚到接近底部时追加下一页。列表没填满一屏（滚动条都没出现）时同样会命中，
        /// 于是继续往下取，直到填满或取完。重复触发由 VM 里的加载守卫挡掉，这里不做去重。
        /// </summary>
        private void OnSuggestionListScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (DataContext is not StatsPageViewModel vm) return;
            if (e.ExtentHeight <= 0 || e.ViewportHeight <= 0) return;

            // 提前约一行的距离开始加载，滚到底时不至于干等。
            if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - SuggestionLoadMoreThreshold) return;

            _ = vm.LoadMoreSuggestionsAsync();
        }

        private const double SuggestionLoadMoreThreshold = 48;
    }
}
