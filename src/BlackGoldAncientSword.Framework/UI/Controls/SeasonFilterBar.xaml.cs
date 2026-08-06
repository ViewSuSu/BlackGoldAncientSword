using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

        /// <summary>模式大类选项：天选/匹配/天人。全局共用一份。</summary>
        public static BindingList<GameModeCategoryOption> CategoryOptions { get; } = new(new[]
        {
            new GameModeCategoryOption(GameModeCategory.Rank),
            new GameModeCategoryOption(GameModeCategory.Match),
            new GameModeCategoryOption(GameModeCategory.Tianren),
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

        // 点击排数/大类单选项：直接写回选中值依赖属性（TwoWay 回传宿主 VM，触发其重查）。
        private void OnTeamSizeClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TeamSizeOption opt)
                SelectedTeamSize = opt.Value;
        }

        private void OnCategoryClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is GameModeCategoryOption opt)
                SelectedCategory = opt.Value;
        }
    }
}
