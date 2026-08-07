using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Views
{
    /// <summary>
    /// 单张队友卡片（完全自包含）：顶部搜索框 + 中部头像/段位/UID + 下部统计行。
    /// 统计行在卡片内部，被卡片背景包裹；卡片之间的 diff 对比由 <see cref="TeamDiffColumn"/>
    /// 独立承载。
    /// <para>
    /// 统计行铺满卡片剩余高度：<see cref="StatItemsSource"/> 变化时同步更新 <see cref="StatCount"/>，
    /// UniformGrid 按 Rows=StatCount 均分各行高度，项数越多每行越矮，底部不留空白。
    /// </para>
    /// <para>
    /// 与 <see cref="TeamDiffColumn"/> 的逐行对齐依赖 WPF <c>SharedSizeGroup</c>：宿主 Grid
    /// 设置 <c>Grid.IsSharedSizeScope="True"</c>，本控件与 diff 列的行定义使用相同的组名
    /// （CardH0/CardH1/CardH2），保证统计行与 diff 行的起始位置和高度完全一致。
    /// </para>
    /// <para>
    /// 数据契约（通过 ElementName=Root 绑定，不依赖宿主 DataContext）：
    /// <list type="bullet">
    ///   <item><see cref="Member"/> — 卡片主体数据（TeamMemberInfo：头像/段位/UID/搜索命令等）。</item>
    ///   <item><see cref="StatItemsSource"/> — 统计行集合，正式页绑 MergedStatRows，测试页绑 mock 行。</item>
    ///   <item><see cref="MemberIndex"/> — 本卡在三栏中的位置（0/1/2），统计行模板据此取对应列值。</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class TeamMemberCard : UserControl
    {
        public TeamMemberCard()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty MemberProperty = DependencyProperty.Register(
            nameof(Member), typeof(TeamMemberInfo), typeof(TeamMemberCard), new PropertyMetadata(null));

        /// <summary>卡片主体数据（TeamMemberInfo）。</summary>
        public TeamMemberInfo? Member
        {
            get => (TeamMemberInfo?)GetValue(MemberProperty);
            set => SetValue(MemberProperty, value);
        }

        public static readonly DependencyProperty StatItemsSourceProperty = DependencyProperty.Register(
            nameof(StatItemsSource), typeof(IEnumerable), typeof(TeamMemberCard),
            new PropertyMetadata(null, OnStatItemsSourceChanged));

        /// <summary>统计行集合（正式页绑 MergedStatRows，测试页绑 mock 行）。</summary>
        public IEnumerable? StatItemsSource
        {
            get => (IEnumerable?)GetValue(StatItemsSourceProperty);
            set => SetValue(StatItemsSourceProperty, value);
        }

        public static readonly DependencyProperty StatCountProperty = DependencyProperty.Register(
            nameof(StatCount), typeof(int), typeof(TeamMemberCard), new PropertyMetadata(0));

        /// <summary>统计行项数，UniformGrid 按此均分行高。</summary>
        public int StatCount
        {
            get => (int)GetValue(StatCountProperty);
            set => SetValue(StatCountProperty, value);
        }

        public static readonly DependencyProperty MemberIndexProperty = DependencyProperty.Register(
            nameof(MemberIndex), typeof(int), typeof(TeamMemberCard), new PropertyMetadata(0));

        /// <summary>本卡在三栏中的位置（0/1/2），统计行模板据此取 MergedStatRow 的 Val0/Val1/Val2。</summary>
        public int MemberIndex
        {
            get => (int)GetValue(MemberIndexProperty);
            set => SetValue(MemberIndexProperty, value);
        }

        private static void OnStatItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TeamMemberCard card) return;
            if (e.OldValue is INotifyCollectionChanged oldIncc)
                oldIncc.CollectionChanged -= card.OnStatCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newIncc)
                newIncc.CollectionChanged += card.OnStatCollectionChanged;
            card.RefreshStatCount();
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

