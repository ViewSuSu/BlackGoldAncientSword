using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    /// <summary>
    /// 通用赛季筛选栏：赛季下拉 + 排数（三排/双排/单排）+ 模式大类（天选/匹配/天人）单选。
    /// 战绩页与队伍信息页共用同一份 UI 与选项数据源，杜绝"某页有数据某页没数据"的口径漂移。
    /// 赛季列表由宿主通过 <see cref="Seasons"/> 传入（统一取自 SeasonCatalog）；排数/大类选项为控件内置静态源。
    /// </summary>
    public partial class SeasonFilterBar : UserControl
    {
        /// <summary>排数选项：三排/双排/单排。全局共用一份，语言切换时统一 ResetBindings。</summary>
        public static BindingList<TeamSizeOption> TeamSizeOptions { get; } = new(new[]
        {
            new TeamSizeOption(TeamSize.Trio),
            new TeamSizeOption(TeamSize.Duo),
            new TeamSizeOption(TeamSize.Solo),
        });

        /// <summary>模式大类选项：只保留天选之人。全局共用一份。</summary>
        public static BindingList<GameModeCategoryOption> CategoryOptions { get; } = new(new[]
        {
            new GameModeCategoryOption(GameModeCategory.Rank),
        });

        private ILocalizationService? _localization;
        private PropertyChangedEventHandler? _onLanguageChanged;

        public SeasonFilterBar()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_localization != null) return;
            try
            {
                _localization = Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<ILocalizationService>();
                _onLanguageChanged = (_, args) =>
                {
                    if (args.PropertyName == nameof(ILocalizationService.CurrentLanguage))
                    {
                        TeamSizeOptions.ResetBindings();
                        CategoryOptions.ResetBindings();
                    }
                };
                _localization.PropertyChanged += _onLanguageChanged;
            }
            catch (System.Exception ex)
            {
                AppLog.Error(ex, $"{nameof(SeasonFilterBar)}.{nameof(OnLoaded)}", "resolve localization failed");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_localization != null && _onLanguageChanged != null)
                _localization.PropertyChanged -= _onLanguageChanged;
            _localization = null;
            _onLanguageChanged = null;
        }

        public static readonly DependencyProperty SeasonsProperty =
            DependencyProperty.Register(nameof(Seasons), typeof(IEnumerable), typeof(SeasonFilterBar),
                new PropertyMetadata(null));

        public IEnumerable? Seasons
        {
            get => (IEnumerable?)GetValue(SeasonsProperty);
            set => SetValue(SeasonsProperty, value);
        }

        public static readonly DependencyProperty SelectedSeasonProperty =
            DependencyProperty.Register(nameof(SelectedSeason), typeof(UnifiedSeason), typeof(SeasonFilterBar),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public UnifiedSeason? SelectedSeason
        {
            get => (UnifiedSeason?)GetValue(SelectedSeasonProperty);
            set => SetValue(SelectedSeasonProperty, value);
        }

        public static readonly DependencyProperty SelectedTeamSizeProperty =
            DependencyProperty.Register(nameof(SelectedTeamSize), typeof(TeamSize), typeof(SeasonFilterBar),
                new FrameworkPropertyMetadata(TeamSize.Trio, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public TeamSize SelectedTeamSize
        {
            get => (TeamSize)GetValue(SelectedTeamSizeProperty);
            set => SetValue(SelectedTeamSizeProperty, value);
        }

        public static readonly DependencyProperty SelectedCategoryProperty =
            DependencyProperty.Register(nameof(SelectedCategory), typeof(GameModeCategory), typeof(SeasonFilterBar),
                new FrameworkPropertyMetadata(GameModeCategory.Rank, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public GameModeCategory SelectedCategory
        {
            get => (GameModeCategory)GetValue(SelectedCategoryProperty);
            set => SetValue(SelectedCategoryProperty, value);
        }

        // 点击排数/大类单选项：写回选中值依赖属性（TwoWay 回传宿主 VM，触发其重查）。
        //
        // 两个关键事实（2026-09-19 用最小 WPF 探针实测，非推理）：
        // ① RadioButton.OnClick 内部用 SetValue(IsChecked=true) 勾选自己——这是对 Mode=OneWay
        //    绑定目标的一次本地写入，会**把 XAML 里的 IsChecked MultiBinding 静默顶掉**
        //    （实测 GetMultiBindingExpression 之后返回 null）。不恢复的话这台按钮从此不再
        //    跟随选中值：程序改值时它保持旧勾选态，界面上出现"双勾"。
        //    所以写完 DP 后必须按 XAML 原样重建该绑定（RestoreIsCheckedBinding）。
        // ② 没设 GroupName 时，WPF **不会**自动把同 ItemsControl 里的兄弟按钮互斥取消
        //    （兄弟按钮的绑定实测保持 Active）——互斥完全由"选中值这一个真源"经绑定驱动：
        //    DP 一变，所有按钮的转换器重新求值，只剩选中项为 true。
        //
        // 另：不设 GroupName 后，点击"已勾选"按钮会把自己取消勾选；写 DP + 重建绑定
        //（转换器立即按当前选中值求值）天然把它按回选中态，不需要单独 rb.IsChecked = true，
        // 那种本地写入恰恰是①里绑定被顶掉的元凶之一。
        private void OnTeamSizeClick(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.DataContext is not TeamSizeOption opt) return;
            SelectedTeamSize = opt.Value;
            RestoreIsCheckedBinding(rb, nameof(SelectedTeamSize));
        }

        private void OnCategoryClick(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.DataContext is not GameModeCategoryOption opt) return;
            SelectedCategory = opt.Value;
            RestoreIsCheckedBinding(rb, nameof(SelectedCategory));
        }

        /// <summary>按 XAML 里的声明重建 <see cref="RadioButton.IsChecked"/> 的 OneWay MultiBinding。
        /// 注意必须用 <see cref="Binding.Source"/> 指向控件自身，不能用 ElementName——
        /// 代码创建的绑定没有 XAML namescope 上下文，ElementName 解析不到，
        /// 转换器会收到 null 而把按钮取消勾选（最小 WPF 探针实测）。</summary>
        private void RestoreIsCheckedBinding(RadioButton rb, string sourceProperty)
        {
            var multi = new MultiBinding
            {
                Converter = (IMultiValueConverter)FindResource("EnumEqualityConverter"),
                Mode = BindingMode.OneWay,
            };
            multi.Bindings.Add(new Binding("Value"));
            multi.Bindings.Add(new Binding(sourceProperty) { Source = this });
            rb.SetBinding(RadioButton.IsCheckedProperty, multi);
        }
    }
}
