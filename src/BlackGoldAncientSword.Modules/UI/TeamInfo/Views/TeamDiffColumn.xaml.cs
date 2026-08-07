using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Views
{
    /// <summary>
    /// 卡片之间的 diff 对比列，与原队伍信息页布局一致：卡 | diff列 | 卡 | diff列 | 卡。
    /// 每行渲染 <see cref="MergedStatRow"/> 一侧的对比值（DiffLeft/DiffRight）。
    /// <para>
    /// 与 <see cref="TeamMemberCard"/> 的统计行逐行对齐依赖两处：
    /// <list type="bullet">
    ///   <item><b>SharedSizeGroup</b>：宿主 Grid 设 <c>Grid.IsSharedSizeScope="True"</c>，
    ///         本控件与卡片用相同组名（CardH0/CardH1/CardH2），保证 diff 行的垂直起始位置与卡片统计行一致。</item>
    ///   <item><b>按项数均分行高</b>：<see cref="StatItemsSource"/> 变化时同步 <see cref="StatCount"/>，
    ///         UniformGrid 按 Rows=StatCount 均分，与卡片统计行行高一致。</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class TeamDiffColumn : UserControl
    {
        public TeamDiffColumn()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty StatItemsSourceProperty = DependencyProperty.Register(
            nameof(StatItemsSource), typeof(IEnumerable), typeof(TeamDiffColumn),
            new PropertyMetadata(null, OnStatItemsSourceChanged));

        /// <summary>统计行集合（正式页绑 MergedStatRows，测试页绑 mock 行）。</summary>
        public IEnumerable? StatItemsSource
        {
            get => (IEnumerable?)GetValue(StatItemsSourceProperty);
            set => SetValue(StatItemsSourceProperty, value);
        }

        public static readonly DependencyProperty StatCountProperty = DependencyProperty.Register(
            nameof(StatCount), typeof(int), typeof(TeamDiffColumn), new PropertyMetadata(0));

        /// <summary>统计行项数，UniformGrid 按此均分行高（与卡片统计行一致）。</summary>
        public int StatCount
        {
            get => (int)GetValue(StatCountProperty);
            set => SetValue(StatCountProperty, value);
        }

        public static readonly DependencyProperty DiffSideProperty = DependencyProperty.Register(
            nameof(DiffSide), typeof(TeamDiffSide), typeof(TeamDiffColumn),
            new PropertyMetadata(TeamDiffSide.Left));

        /// <summary>取统计行哪一侧的对比值（Left = DiffLeftText，Right = DiffRightText）。</summary>
        public TeamDiffSide DiffSide
        {
            get => (TeamDiffSide)GetValue(DiffSideProperty);
            set => SetValue(DiffSideProperty, value);
        }

        private static void OnStatItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TeamDiffColumn column) return;
            if (e.OldValue is INotifyCollectionChanged oldIncc)
                oldIncc.CollectionChanged -= column.OnStatCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newIncc)
                newIncc.CollectionChanged += column.OnStatCollectionChanged;
            column.RefreshStatCount();
        }

        private void OnStatCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshStatCount();
        }

        private void RefreshStatCount()
        {
            StatCount = StatItemsSource is ICollection { Count: > 0 } col ? col.Count : 0;
        }
    }
}
