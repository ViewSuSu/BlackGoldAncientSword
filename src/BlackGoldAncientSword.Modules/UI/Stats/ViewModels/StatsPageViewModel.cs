using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.UI.Controls;
using System.ComponentModel;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.Core.Events;
using System.Collections.Generic;
using System.Runtime;
using BlackGoldAncientSword.Modules.UI.Stats.Services;


namespace BlackGoldAncientSword.Modules.UI.Stats.ViewModels
{
    public class StatsPageViewModel : ViewModelBase
    {
        private readonly IPlayerPrefsService _playerPrefsService;
        private readonly ITipMessageService _tipMessage;
        private readonly ILocalizationService _localizationService;
        private readonly IClipboardService _clipboard;
        private readonly PlayerStatsLoader _playerStatsLoader;
        private readonly BattleListLoader _battleListLoader;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly PropertyChangedEventHandler? _onLanguageChangedHandler;
        private CancellationTokenSource? _loadAllCts;
        private CancellationTokenSource? _loadStatsCts;

        public StatsPageViewModel(
            IPlayerPrefsService playerPrefsService,
            ILocalizationService localizationService,
            ITipMessageService tipMessageService,
            IClipboardService clipboard,
            PlayerStatsLoader playerStatsLoader,
            BattleListLoader battleListLoader,
            IUIDispatcher uiDispatcher,
            ILocalizedTextProvider localizedText)
        {
            _playerPrefsService = playerPrefsService;
            _tipMessage = tipMessageService;
            _localizationService = localizationService;
            _clipboard = clipboard;
            _playerStatsLoader = playerStatsLoader;
            _battleListLoader = battleListLoader;
            _uiDispatcher = uiDispatcher;
            _localizedText = localizedText;
            _onLanguageChangedHandler = OnLanguageChanged;
            _localizationService.PropertyChanged += _onLanguageChangedHandler;
            Seasons = new ObservableCollection<UnifiedSeason>();
            DetailStats = new ObservableCollection<StatEntryItem>();
            RecentBattles = new RangeObservableCollection<RecentBattleDisplayItem>();
        }

        // === Player Info ===
        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set
            {
                if (_userName == value) return;
                _userName = value;
                RaisePropertyChanged(nameof(UserName));
                RaisePropertyChanged(nameof(IsLocalUser));
            }
        }

        private string _uid = string.Empty;
        public string UID
        {
            get => _uid;
            set
            {
                if (_uid == value) return;
                _uid = value;
                RaisePropertyChanged(nameof(UID));
            }
        }

        private string _level = string.Empty;
        public string Level
        {
            get => _level;
            set
            {
                if (_level == value) return;
                _level = value;
                RaisePropertyChanged(nameof(Level));
            }
        }

        private string _avatarUrl = string.Empty;
        public string AvatarUrl
        {
            get => _avatarUrl;
            set
            {
                if (_avatarUrl == value) return;
                _avatarUrl = value;
                RaisePropertyChanged(nameof(AvatarUrl));
            }
        }

        private DelegateCommand? _copyUserNameCommand;
        public DelegateCommand CopyUserNameCommand =>
            _copyUserNameCommand ??= new DelegateCommand(() =>
            {
                _clipboard.TrySetText(UserName);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(_localizedText.Get("Stats.CopySuccess", "复制成功")));
            });

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                RaisePropertyChanged(nameof(SearchText));
            }
        }

        private readonly SearchDebounceGate _searchDebounce = new();

        private DelegateCommand? _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return;
                if (!_searchDebounce.TryEnter())
                {
                    _tipMessage.ShowError(L("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }
                _playerPrefsService.Current.PlayerName = SearchText.Trim();
                await RefreshAllAsync();
            });


        public bool IsLocalUser =>
            !string.IsNullOrEmpty(UserName) &&
            !string.IsNullOrEmpty(_playerPrefsService.Current.OriginalPlayerName) &&
            string.Equals(UserName, _playerPrefsService.Current.OriginalPlayerName, StringComparison.OrdinalIgnoreCase);

        private DelegateCommand? _goBackToMeCommand;
        public DelegateCommand GoBackToMeCommand =>
            _goBackToMeCommand ??= new DelegateCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(_playerPrefsService.Current.OriginalPlayerName))
                {
                    _tipMessage.ShowError(L("Stats.NoLocalUser", "未检测到本地用户信息"));
                    return;
                }
                if (!_searchDebounce.TryEnter())
                {
                    _tipMessage.ShowError(L("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }
                // 查询身份仍用本地登录名（PlayerName == OriginalPlayerName 触发 LoadAllAsync 的
                // UID 优先分支）；搜索框展示本地 UID（player_id）——UID 存在时显示 UID，否则回退显示名字。
                _playerPrefsService.Current.PlayerName = _playerPrefsService.Current.OriginalPlayerName;
                var localUid = _playerPrefsService.Current.PlayerId;
                SearchText = !string.IsNullOrEmpty(localUid)
                    ? localUid
                    : _playerPrefsService.Current.OriginalPlayerName;
                await RefreshAllAsync();
            });

        private DelegateCommand? _copyUIDCommand;

        public DelegateCommand CopyUIDCommand =>
            _copyUIDCommand ??= new DelegateCommand(() =>
            {
                _clipboard.TrySetText(UID);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(_localizedText.Get("Stats.CopySuccess", "复制成功")));
            });

        private DelegateCommand? _refreshAllCommand;
        public DelegateCommand RefreshAllCommand =>
            _refreshAllCommand ??= new DelegateCommand(async () => await RefreshAllAsync());


        // === Rank ===
        private string _rankName = string.Empty;
        public string RankName
        {
            get => _rankName;
            set
            {
                if (_rankName == value) return;
                _rankName = value;
                RaisePropertyChanged(nameof(RankName));
            }
        }

        private string _rankIcon = string.Empty;
        public string RankIcon
        {
            get => _rankIcon;
            set
            {
                if (_rankIcon == value) return;
                _rankIcon = value;
                RaisePropertyChanged(nameof(RankIcon));
            }
        }

        private double _rankScore;
        public double RankScore
        {
            get => _rankScore;
            set
            {
                if (_rankScore == value) return;
                _rankScore = value;
                RaisePropertyChanged(nameof(RankScore));
            }
        }

        private string _rankLevel = string.Empty;
        public string RankLevel
        {
            get => _rankLevel;
            set
            {
                if (_rankLevel == value) return;
                _rankLevel = value;
                RaisePropertyChanged(nameof(RankLevel));
            }
        }

        private double _rankTierScore;
        public double RankTierScore
        {
            get => _rankTierScore;
            set
            {
                if (_rankTierScore == value) return;
                _rankTierScore = value;
                RaisePropertyChanged(nameof(RankTierScore));
            }
        }

        private string _pageRankName = string.Empty;
        public string PageRankName
        {
            get => _pageRankName;
            set
            {
                if (_pageRankName == value) return;
                _pageRankName = value;
                RaisePropertyChanged(nameof(PageRankName));
            }
        }

        private int _pageStarCount;
        public int PageStarCount
        {
            get => _pageStarCount;
            set
            {
                if (_pageStarCount == value) return;
                _pageStarCount = value;
                RaisePropertyChanged(nameof(PageStarCount));
            }
        }

        private bool _pageHasStars;
        public bool PageHasStars
        {
            get => _pageHasStars;
            set
            {
                if (_pageHasStars == value) return;
                _pageHasStars = value;
                RaisePropertyChanged(nameof(PageHasStars));
            }
        }


        // === Rank Stats ===
        private string _totalGames = "0";
        public string TotalGames
        {
            get => _totalGames;
            set
            {
                if (_totalGames == value) return;
                _totalGames = value;
                RaisePropertyChanged(nameof(TotalGames));
            }
        }

        private string _topOneCount = "0";
        public string TopOneCount
        {
            get => _topOneCount;
            set
            {
                if (_topOneCount == value) return;
                _topOneCount = value;
                RaisePropertyChanged(nameof(TopOneCount));
            }
        }

        private string _topFiveCount = "0";
        public string TopFiveCount
        {
            get => _topFiveCount;
            set
            {
                if (_topFiveCount == value) return;
                _topFiveCount = value;
                RaisePropertyChanged(nameof(TopFiveCount));
            }
        }

        private string _avgDamage = "0";
        public string AvgDamage
        {
            get => _avgDamage;
            set
            {
                if (_avgDamage == value) return;
                _avgDamage = value;
                RaisePropertyChanged(nameof(AvgDamage));
            }
        }

        // === Filters ===
        private GameModeCategory _selectedCategory = GameModeCategory.Rank;
        public GameModeCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory == value) return;
                _selectedCategory = value;
                RaisePropertyChanged(nameof(SelectedCategory));
                RefreshStats();
            }
        }

        private TeamSize _selectedTeamSize = TeamSize.Trio;
        public TeamSize SelectedTeamSize
        {
            get => _selectedTeamSize;
            set
            {
                if (_selectedTeamSize == value) return;
                _selectedTeamSize = value;
                RaisePropertyChanged(nameof(SelectedTeamSize));
                RefreshStats();
            }
        }


        private static readonly Dictionary<string, string> StatKeyToResourceKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["round"] = "Stats.Matches",
            ["win"] = "Stats.FirstPlace",
            ["top5"] = "Stats.TopFive",
            ["avg_damage"] = "Stats.AvgDamage",
            ["kd"] = "Stats.KD",
            ["win_rate"] = "Stats.FirstRate",
            ["top5_rate"] = "Stats.TopFiveRate",
            ["max_shock_count"] = "Stats.MostParry",
            ["avg_kill"] = "Stats.AvgKills",
            ["avg_cure"] = "Stats.AvgHeal",
            ["avg_assist"] = "Stats.AvgAssists",
            ["avg_total_live_time"] = "Stats.AvgSurvival",
            ["max_kill"] = "Stats.BestKills",
            ["max_cure"] = "Stats.BestHeal",
            ["max_assist"] = "Stats.BestAssists",
            ["max_damage"] = "Stats.BestDamage",
              ["avg_move_distance"] = "Stats.AvgMoveDistance",
              ["max_move_distance"] = "Stats.MaxMoveDistance",
        };

        private UnifiedSeason? _selectedSeason;
        public UnifiedSeason? SelectedSeason
        {
            get => _selectedSeason;
            set
            {
                if (_selectedSeason == value) return;
                _selectedSeason = value;
                RaisePropertyChanged(nameof(SelectedSeason));
                RefreshStats();
            }
        }

        // === Collections ===
        public ObservableCollection<UnifiedSeason> Seasons { get; }
        public ObservableCollection<StatEntryItem> DetailStats { get; }
        public RangeObservableCollection<RecentBattleDisplayItem> RecentBattles { get; }

        // 最近对局全量缓存：后端一次性返回的所有对局（modeCode=null）。RecentBattles 是它按
        // 下拉筛选后的视图；筛选纯前端内存过滤，与网页端一致，不重新请求。
        private readonly List<RecentBattleDisplayItem> _allBattles = new();

        // === 最近对局模式筛选（仿网页端级联下拉）===
        // 可空表示"该维度不约束"；两者皆 null = 无筛选（默认，看全部）。与统计区的非空
        // SelectedCategory/SelectedTeamSize 完全隔离，互不影响。
        private GameModeCategory? _selectedBattleCategory;
        public GameModeCategory? SelectedBattleCategory
        {
            get => _selectedBattleCategory;
            set
            {
                if (_selectedBattleCategory == value) return;
                _selectedBattleCategory = value;
                RaisePropertyChanged(nameof(SelectedBattleCategory));
                ApplyBattleFilter();
            }
        }

        private TeamSize? _selectedBattleTeamSize;
        public TeamSize? SelectedBattleTeamSize
        {
            get => _selectedBattleTeamSize;
            set
            {
                if (_selectedBattleTeamSize == value) return;
                _selectedBattleTeamSize = value;
                RaisePropertyChanged(nameof(SelectedBattleTeamSize));
                ApplyBattleFilter();
            }
        }

        private bool _isBattleFilterOpen;
        public bool IsBattleFilterOpen
        {
            get => _isBattleFilterOpen;
            set
            {
                if (_isBattleFilterOpen == value) return;
                _isBattleFilterOpen = value;
                RaisePropertyChanged(nameof(IsBattleFilterOpen));
                if (!value) HoveredBattleCategory = null; // 收起主下拉时一并收起二级

            }
        }

        // 仅控制二级（排数）子菜单的展开，不参与筛选：鼠标悬停到某个一级大类时置为该大类，
        // 二级面板据此显示；鼠标移出一级列时清空、收起二级。
        private GameModeCategory? _hoveredBattleCategory;
        public GameModeCategory? HoveredBattleCategory
        {
            get => _hoveredBattleCategory;
            set
            {
                if (_hoveredBattleCategory == value) return;
                _hoveredBattleCategory = value;
                RaisePropertyChanged(nameof(HoveredBattleCategory));
                RaisePropertyChanged(nameof(IsBattleSubMenuOpen));
            }
        }

        /// <summary>二级子菜单是否展开（悬停到任一一级大类时展开）。</summary>
        public bool IsBattleSubMenuOpen => _hoveredBattleCategory != null;

        private DelegateCommand<GameModeCategory?>? _hoverBattleCategoryCommand;
        public DelegateCommand<GameModeCategory?> HoverBattleCategoryCommand =>
            _hoverBattleCategoryCommand ??= new DelegateCommand<GameModeCategory?>(cat =>
            {
                HoveredBattleCategory = cat;
            });

        private bool _hasNoBattleResult;
        public bool HasNoBattleResult
        {
            get => _hasNoBattleResult;
            set
            {
                if (_hasNoBattleResult == value) return;
                _hasNoBattleResult = value;
                RaisePropertyChanged(nameof(HasNoBattleResult));
            }
        }

        /// <summary>
        /// 下拉按钮当前文字：无筛选显示"最近对局"；两维都选显示组合模式名（如"天选三排"）；
        /// 只选一维显示那一维的名字。走本地化资源，语言切换时随 <see cref="BattleFilterDisplayText"/> 通知刷新。
        /// </summary>
        public string BattleFilterDisplayText
        {
            get
            {
                if (_selectedBattleCategory == null && _selectedBattleTeamSize == null)
                    return L("Stats.RecentBattles", "最近对局");
                if (_selectedBattleCategory != null && _selectedBattleTeamSize != null)
                {
                    var gm = GameModeExtensions.FromCategoryAndTeamSize(
                        _selectedBattleCategory.Value, _selectedBattleTeamSize.Value);
                    return _localizedText.Get("GameMode." + gm, gm.ToString());
                }
                if (_selectedBattleCategory != null)
                    return _localizedText.Get("GameMode." + _selectedBattleCategory.Value, _selectedBattleCategory.Value.ToString());
                return _localizedText.Get("GameMode." + _selectedBattleTeamSize!.Value, _selectedBattleTeamSize.Value.ToString());
            }
        }

        private DelegateCommand? _resetBattleFilterCommand;
        public DelegateCommand ResetBattleFilterCommand =>
            _resetBattleFilterCommand ??= new DelegateCommand(() =>
            {
                _selectedBattleCategory = null;
                _selectedBattleTeamSize = null;
                RaisePropertyChanged(nameof(SelectedBattleCategory));
                RaisePropertyChanged(nameof(SelectedBattleTeamSize));
                IsBattleFilterOpen = false;
                ApplyBattleFilter();
            });

        // 点击一级/二级项：再次点已选中的项则取消该维度（切换语义）。
        // 取消大类时同时取消排数——"天选"没了，"天选三排"里的三排也失去归属，回到全部更符合直觉。
        private DelegateCommand<GameModeCategory?>? _selectBattleCategoryCommand;
        public DelegateCommand<GameModeCategory?> SelectBattleCategoryCommand =>
            _selectBattleCategoryCommand ??= new DelegateCommand<GameModeCategory?>(cat =>
            {
                if (cat == null) return;
                if (_selectedBattleCategory == cat.Value)
                {
                    // 取消大类 → 连同排数一起清空，回到全部
                    _selectedBattleCategory = null;
                    _selectedBattleTeamSize = null;
                    RaisePropertyChanged(nameof(SelectedBattleCategory));
                    RaisePropertyChanged(nameof(SelectedBattleTeamSize));
                    ApplyBattleFilter();
                }
                else
                {
                    SelectedBattleCategory = cat.Value;
                }
            });

        private DelegateCommand<TeamSize?>? _selectBattleTeamSizeCommand;
        public DelegateCommand<TeamSize?> SelectBattleTeamSizeCommand =>
            _selectBattleTeamSizeCommand ??= new DelegateCommand<TeamSize?>(size =>
            {
                if (size == null) return;

                // 选二级排数时，把当前悬停的一级大类一并选上——用户是"悬停天选 → 点三排"来表达"天选三排"，
                // 不能只设排数而丢掉大类（否则会筛成所有模式的三排）。悬停态由二级子菜单的展开来源保证非空。
                if (_hoveredBattleCategory != null && _selectedBattleCategory != _hoveredBattleCategory)
                {
                    _selectedBattleCategory = _hoveredBattleCategory;
                    RaisePropertyChanged(nameof(SelectedBattleCategory));
                }

                // 再次点已选排数 → 仅取消排数，保留大类（如"天选三排"点三排 → "天选"全部）
                SelectedBattleTeamSize = _selectedBattleTeamSize == size.Value ? null : size.Value;
            });

        // === 打开对局详情 Overlay ===
        // 传整行 RecentBattleDisplayItem，把已经算好的段位/星数/分差直接透传给 BattleDetail，
        // 避免在详情侧重复实现段位计算逻辑。
        private DelegateCommand<RecentBattleDisplayItem>? _openBattleDetailCommand;
        public DelegateCommand<RecentBattleDisplayItem> OpenBattleDetailCommand =>
            _openBattleDetailCommand ??= new DelegateCommand<RecentBattleDisplayItem>(row =>
            {
                if (row == null || string.IsNullOrWhiteSpace(row.BattleId) || row.BattleId == "0")
                    return;

                // 按需加载 BattleDetailModule
                try
                {
                    var moduleManager = containerProvider.Resolve<IModuleManager>();
                    moduleManager.LoadModule(nameof(PageNames.BattleDetailPage).Replace("Page", "Module"));
                }
                catch { }

                var parameters = new NavigationParameters
                {
                    { nameof(PageNames.BattleDetailPage), row.BattleId },
                    { "RoleId", _roleId },
                    { "DataSource", (int)(_sourceContext?.Source ?? DataSource.MiniProgram) },
                    { "GameMode", row.GameMode },
                    { "ModeCategoryText", row.GameModeCategoryText },
                    { "ModeTeamSizeText", row.GameModeTeamSizeText },
                    { "RankDisplayText", row.RankDisplayText },
                    { "StarCount", row.StarCount },
                    { "HasStars", row.HasStars },
                    { "ScoreNumber", row.ScoreNumber },
                    { "ScoreDiff", row.ScoreDiff },
                    { "ScoreDiffDisplay", row.ScoreDiffDisplay },
                    { "ShowScoreNumber", row.ShowScoreNumber },
                    { "IsRankMode", row.IsRankMode },
                };
                regionManager.RequestNavigate(GlobalConstant.BattleDetailRegion, PageNames.BattleDetailPage, parameters);
            });

                // === Per-section loading states ===
        private bool _isPlayerInfoLoading;
        public bool IsPlayerInfoLoading
        {
            get => _isPlayerInfoLoading;
            set
            {
                if (_isPlayerInfoLoading == value) return;
                _isPlayerInfoLoading = value;
                RaisePropertyChanged(nameof(IsPlayerInfoLoading));
            }
        }

        private double _playerInfoProgress;
        public double PlayerInfoProgress
        {
            get => _playerInfoProgress;
            set
            {
                if (_playerInfoProgress == value) return;
                _playerInfoProgress = value;
                RaisePropertyChanged(nameof(PlayerInfoProgress));
            }
        }

        private bool _isRecentBattlesLoading;
        public bool IsRecentBattlesLoading
        {
            get => _isRecentBattlesLoading;
            set
            {
                if (_isRecentBattlesLoading == value) return;
                _isRecentBattlesLoading = value;
                RaisePropertyChanged(nameof(IsRecentBattlesLoading));
            }
        }

        private double _recentBattlesProgress;
        public double RecentBattlesProgress
        {
            get => _recentBattlesProgress;
            set
            {
                if (_recentBattlesProgress == value) return;
                _recentBattlesProgress = value;
                RaisePropertyChanged(nameof(RecentBattlesProgress));
            }
        }

        private bool _isStatsLoading;
        public bool IsStatsLoading
        {
            get => _isStatsLoading;
            set
            {
                if (_isStatsLoading == value) return;
                _isStatsLoading = value;
                RaisePropertyChanged(nameof(IsStatsLoading));
            }
        }

        private double _statsProgress;
        public double StatsProgress
        {
            get => _statsProgress;
            set
            {
                if (_statsProgress == value) return;
                _statsProgress = value;
                RaisePropertyChanged(nameof(StatsProgress));
            }
        }

        private bool _showNotFound;
        public bool ShowNotFound
        {
            get => _showNotFound;
            set
            {
                if (_showNotFound == value) return;
                _showNotFound = value;
                RaisePropertyChanged(nameof(ShowNotFound));
            }
        }

        private string _roleId = string.Empty;
        private PlayerSourceContext? _sourceContext;

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
            {
                // 排数/大类选项文案由 SeasonFilterBar 控件自行 ResetBindings；此处只刷新本页专有的对局筛选文案。
                RaisePropertyChanged(nameof(BattleFilterDisplayText));
            }
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);

            // 队友卡片点"查看战绩"时通过导航参数携带目标玩家名。此时必须查该玩家，
            // 不能 reload player_prefs（否则 Current.PlayerName 会被重置回本地账号，串到自己）。
            var targetPlayer = navigationContext.Parameters
                .GetValue<string>(NavigationParameterKeys.TargetPlayerName);

            // 基类签名为 void，无法 await——把"确定目标玩家 + 刷新 UI + 拉战绩"整块塞进
            // fire-and-forget async。RefreshAllAsync 内部已有 try/catch 兜底。
            _ = LoadForTargetAndRefreshAsync(targetPlayer);
        }

        /// <summary>
        /// 确定要查询的玩家并刷新战绩。
        /// <para><paramref name="targetPlayer"/> 非空（来自队友卡片导航参数）：直接查该玩家，
        /// 不重读 player_prefs，避免把队友名冲成本地账号。</para>
        /// <para><paramref name="targetPlayer"/> 为空（底部导航/搜索页正常进入）：实时重读本地登录用户
        /// —— Steam ↔ 网易 客户端共用同一份 player_prefs.txt，用户在游戏内切了客户端后构造时缓存的
        /// <c>Current</c> 会陈旧，主动 reload 一次避免搜索框/战绩查询用错账号。</para>
        /// </summary>
        private async System.Threading.Tasks.Task LoadForTargetAndRefreshAsync(string? targetPlayer)
        {
            if (!string.IsNullOrWhiteSpace(targetPlayer))
            {
                _playerPrefsService.Current.PlayerName = targetPlayer.Trim();
            }
            else
            {
                try
                {
                    await _playerPrefsService.LoadAsync();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(StatsPageViewModel)}.{nameof(LoadForTargetAndRefreshAsync)}", "reload prefs failed");
                }
            }

            RaisePropertyChanged(nameof(IsLocalUser));
            // 查本地用户（targetPlayer 为空，非队友卡片跳转）时搜索框展示本地 UID（player_id），
            // 与「回到我」一致——UID 唯一可查、不重名；队友跳转仍显示队友名字。
            var localUid = _playerPrefsService.Current.PlayerId;
            SearchText = string.IsNullOrWhiteSpace(targetPlayer) && !string.IsNullOrEmpty(localUid)
                ? localUid
                : _playerPrefsService.Current.PlayerName;
            await RefreshAllAsync();
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            // 语言事件订阅在 ctor 中绑定、在 Dispose 中解绑——单例 VM 跨多次导航复用同一实例，
            // 不能在此处解绑：原实现首次离开页面后再回来时，本地化资源切换不会再触发统计标签刷新。
            CancelAndDispose(ref _loadAllCts);
            CancelAndDispose(ref _loadStatsCts);
            ClearImageBindings();
            base.OnNavigatedFromExecute(navigationContext);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_localizationService != null && _onLanguageChangedHandler != null)
                    _localizationService.PropertyChanged -= _onLanguageChangedHandler;
                CancelAndDispose(ref _loadAllCts);
                CancelAndDispose(ref _loadStatsCts);
            }
            base.Dispose(disposing);
        }

        private RecentBattleDisplayItem BuildBattleDisplayItem(UnifiedRecentBattleItem b)
        {
            var modeCode = b.GameMode;
            return new RecentBattleDisplayItem
            {
                BattleId = b.BattleId,
                Rank = b.Rank,
                HeroIcon = b.HeroIcon,
                HeroName = string.IsNullOrEmpty(b.HeroName) ? "Unknown" : b.HeroName,
                GameModeText = ResolveModeName(b),
                GameModeCategoryText = ResolveModeCategoryText(b),
                GameModeTeamSizeText = ResolveModeTeamSizeText(b),
                GameMode = modeCode,
                IsRankMode = IsTianxuanMode(modeCode),
                Kill = b.Kill,
                Damage = b.Damage,
                ScoreNumber = GetRankTierScore(b.RoundRankScore, modeCode),
                // 分差直接采用 unified 后端算好的 score.delta，不再客户端 end-begin 相减。
                ScoreDiff = b.ScoreDelta,
                RankDisplayText = GetRankNameForScore(b.RoundRankScore, modeCode) + GetSubTierName(b.RoundRankScore, IsTianxuanMode(modeCode)),
                ShowScoreNumber = ShouldShowTierScore(b.RoundRankScore, modeCode),
                StarCount = GetStarCount(b.RoundRankScore, modeCode),
                HasStars = IsTianxuanMode(modeCode) && b.RoundRankScore >= 4500,
                ScoreDiffDisplay = b.ScoreDelta == 0 && b.BeginRankScore == null
                    ? string.Empty
                    : FormatScoreDiff(b.ScoreDelta),
                BattleTime = FormatUnixTime(b.BattleEndTimeMs),
                Rating = b.Rating ?? string.Empty,
            };
        }

        private async void RefreshStats()
        {
            CancelAndDispose(ref _loadStatsCts);
            _loadStatsCts = new CancellationTokenSource();
            await LoadStatsAsync(_loadStatsCts.Token);
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            try { cts.Dispose(); } catch (ObjectDisposedException) { }
            cts = null;
        }

        private void ClearImageBindings()
        {
            AvatarUrl = string.Empty;
            RankIcon = string.Empty;
        }

        private void ClearAllData()
        {
            UserName = string.Empty;
            UID = string.Empty;
            Level = string.Empty;
            AvatarUrl = string.Empty;
            RankName = string.Empty;
            RankIcon = string.Empty;
            RankScore = 0;
            RankLevel = string.Empty;
            PageRankName = string.Empty;
            PageStarCount = 0;
            PageHasStars = false;
            RankTierScore = 0;
            TotalGames = "0";
            TopOneCount = "0";
            TopFiveCount = "0";
            AvgDamage = "0";
            DetailStats.Clear();
            RecentBattles.Clear();
            _allBattles.Clear();
            _selectedBattleCategory = null;
            _selectedBattleTeamSize = null;
            RaisePropertyChanged(nameof(SelectedBattleCategory));
            RaisePropertyChanged(nameof(SelectedBattleTeamSize));
            RaisePropertyChanged(nameof(BattleFilterDisplayText));
            IsBattleFilterOpen = false;
            HasNoBattleResult = false;
        }

        /// <summary>
        /// 按当前下拉选中的大类/排数（可空=不约束）从 <see cref="_allBattles"/> 过滤并刷新
        /// <see cref="RecentBattles"/>。无筛选时显示全部（含无法归类的"未知模式"行）；一旦选了任一
        /// 具体维度，无法解析模式的行被排除（选具体模式时不该混入未知模式）。
        /// </summary>
        private void ApplyBattleFilter()
        {
            var noFilter = _selectedBattleCategory == null && _selectedBattleTeamSize == null;
            var filtered = new List<RecentBattleDisplayItem>(_allBattles.Count);
            foreach (var item in _allBattles)
            {
                var gm = TryResolveGameMode(item.GameMode);
                if (gm == null)
                {
                    if (noFilter) filtered.Add(item);
                    continue;
                }

                GameModeCategory cat;
                TeamSize size;
                try
                {
                    cat = gm.Value.GetCategory();
                    size = gm.Value.GetTeamSize();
                }
                catch (ArgumentOutOfRangeException)
                {
                    if (noFilter) filtered.Add(item);
                    continue;
                }

                if ((_selectedBattleCategory == null || _selectedBattleCategory == cat)
                    && (_selectedBattleTeamSize == null || _selectedBattleTeamSize == size))
                    filtered.Add(item);
            }

            RecentBattles.ReplaceAll(filtered);
            HasNoBattleResult = _allBattles.Count > 0 && filtered.Count == 0;
            RaisePropertyChanged(nameof(BattleFilterDisplayText));
        }

        private async System.Threading.Tasks.Task RefreshAllAsync()
        {
            CancelAndDispose(ref _loadAllCts);
            _loadAllCts = new CancellationTokenSource();
            var ct = _loadAllCts.Token;

            IsPlayerInfoLoading = true;
            PlayerInfoProgress = 0;
            IsRecentBattlesLoading = true;
            RecentBattlesProgress = 0;
            IsStatsLoading = true;
            StatsProgress = 0;

            var success = await LoadAllAsync(ct);

            if (!ct.IsCancellationRequested && success)
            {
                _tipMessage.ShowInfo(L("Stats.SearchSuccess", "搜索成功"));
            }
            // 失败路径：LoadAllAsync 内部已按响应体 msg 弹窗（NarakaApiException.Msg），
            // 这里不再补一次前端兜底文案，避免"网络问题"这种猜测性文案覆盖真实的后端错误。
        }

        private string L(string key, string fallback) => _localizedText.Get(key, fallback);

        private async System.Threading.Tasks.Task<bool> LoadAllAsync(CancellationToken ct)
        {
            ShowNotFound = false;
            if (!_playerPrefsService.Current.IsLoaded)
            {
                ShowNotFound = true;
                ClearAllData();
                return false;
            }

            var localName = _playerPrefsService.Current.PlayerName;
            if (string.IsNullOrEmpty(localName))
            {
                ClearAllData();
                return false;
            }

            IsPlayerInfoLoading = true;
            PlayerInfoProgress = 0;
            IsRecentBattlesLoading = true;
            RecentBattlesProgress = 0;
            IsStatsLoading = true;
            StatsProgress = 0;

            try
            {
                // 查的是本地用户时优先用本地 UID（player_prefs 的 player_id）：UID 唯一、不重名，
                // 用户名可能重名/查无。判断"是否本地用户"用 PlayerName == OriginalPlayerName。
                var localUid =
                    !string.IsNullOrEmpty(_playerPrefsService.Current.OriginalPlayerName)
                    && string.Equals(localName, _playerPrefsService.Current.OriginalPlayerName, StringComparison.OrdinalIgnoreCase)
                        ? _playerPrefsService.Current.PlayerId
                        : null;

                var search = await _playerStatsLoader.SearchRoleByUidThenNameAsync(localUid, localName, ct);
                if (search == null || string.IsNullOrEmpty(search.RoleIdSimple))
                {
                    ShowNotFound = true;
                    ClearAllData();
                    _tipMessage.ShowError(L("Stats.PlayerNotFound", "未找到该玩家，请检查名称是否正确"));
                    return false;
                }
                _roleId = search.RoleIdSimple;
                _sourceContext = new PlayerSourceContext(_roleId, search.DataSource);
                var ctx = _sourceContext;

                // 三块数据（玩家信息 / 赛季 / 对局列表）彼此无依赖：并行发起，且各自完成即绑定自己的 UI，
                // 不再 WhenAll 干等最慢的一路。原实现等三者全回来才统一绑定，最慢的 battles（实测约 5s）
                // 把 userInfo（约 1s）也拖成 5s 才显示——用户整页转圈无法操作。拆开后玩家信息一秒即出，
                // 对局列表区自己转圈，谁快谁先亮。三个 Apply* 各自 try/catch + 关自己的 loading，互不影响。
                var userInfoApply = ApplyUserInfoAsync(ctx, localName, ct);
                var seasonsApply = ApplySeasonsAsync(ct);
                var battlesApply = ApplyBattlesAsync(ctx, ct);

                // search 已成功即代表"找到玩家"，"搜索成功"提示不必等三块数据全部绑定完。
                // 等三条续接结束仅为让 RefreshAllAsync 的成功判定在数据落地后返回。
                await System.Threading.Tasks.Task.WhenAll(userInfoApply, seasonsApply, battlesApply);
                return true;
            }
            catch (OperationCanceledException)
            {
                // 导航离开或过滤条件已变更——不是错误
                return false;
            }
            catch (NarakaApiException ex)
            {
                AppLog.Error(ex, "StatsPage", "LoadAllAsync api error");
                // 搜索被后端拒绝（429/401/500 等）——渲染态与"没查到"分支保持一致：清空显示，
                // 这样 IsLocalUser 会随 UserName 一并变 false，"回到我"按钮才不会误判为"你正在自己页面"。
                ClearAllData();
                // 只在后端返回了 msg 时才弹；msg 为空按约定静默，不拼前端兜底文案。
                if (!string.IsNullOrEmpty(ex.Msg))
                    _tipMessage.ShowError(ex.Msg!);
                return false;
            }
            catch (Exception ex)
            {
                // 未知底层异常（网络中断/反序列化失败等），无 msg 可展示，仅记日志。
                AppLog.Error(ex, "StatsPage", "LoadAllAsync failed");
                ClearAllData();
                return false;
            }
        }

        /// <summary>玩家信息（昵称/等级/UID/头像）：拉取后立即绑定，与赛季/对局互不阻塞。</summary>
        private async System.Threading.Tasks.Task ApplyUserInfoAsync(
            PlayerSourceContext ctx, string localName, CancellationToken ct)
        {
            try
            {
                var userInfo = await _playerStatsLoader.FetchUserInfoAsync(ctx, ct);
                ct.ThrowIfCancellationRequested();
                if (userInfo != null)
                {
                    UserName = string.IsNullOrEmpty(userInfo.RoleName) ? localName : userInfo.RoleName;
                    Level = $"Lv.{(int)userInfo.RoleLevel}";
                    UID = userInfo.Uid;
                    AvatarUrl = userInfo.HeadIcon;
                }
                PlayerInfoProgress = 100;
            }
            catch (OperationCanceledException) { }
            catch (NarakaApiException ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplyUserInfoAsync)} api error");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplyUserInfoAsync)} failed");
            }
            finally
            {
                IsPlayerInfoLoading = false;
            }
        }

        /// <summary>赛季列表：绑定后选中当前赛季，触发 LoadStatsAsync 拉取该赛季统计。</summary>
        private async System.Threading.Tasks.Task ApplySeasonsAsync(CancellationToken ct)
        {
            try
            {
                var seasonsResult = await _playerStatsLoader.FetchSeasonsAsync(ct);
                ct.ThrowIfCancellationRequested();
                Seasons.Clear();
                if (seasonsResult != null)
                    foreach (var s in seasonsResult) Seasons.Add(s);

                if (Seasons.Count > 0)
                {
                    // SelectedSeason 赋值会触发 RefreshStats → LoadStatsAsync，由其 finally 关闭 IsStatsLoading。
                    // 但重复查同一玩家时新旧值可能相等（setter 短路不触发 RefreshStats），会让 IsStatsLoading
                    // 悬空转圈——此时显式刷新一次统计。
                    var target = Seasons[0];
                    if (Equals(SelectedSeason, target))
                        RefreshStats();
                    else
                        SelectedSeason = target;
                }
                else
                {
                    // 无赛季则不会触发 LoadStatsAsync，需在此关闭统计区 loading，避免永久转圈。
                    IsStatsLoading = false;
                }
            }
            catch (OperationCanceledException) { }
            catch (NarakaApiException ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplySeasonsAsync)} api error");
                IsStatsLoading = false;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplySeasonsAsync)} failed");
                IsStatsLoading = false;
            }
        }

        /// <summary>对局列表：拉取后一次性构造全部行并 ReplaceAll（仅一次 Reset 通知），与其它两块互不阻塞。</summary>
        private async System.Threading.Tasks.Task ApplyBattlesAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            try
            {
                var battlesResult = await _battleListLoader.FetchBattleListAsync(ctx, ct);
                ct.ThrowIfCancellationRequested();
                if (battlesResult != null)
                {
                    // 展示后端 matches 单页返回的全部对局（与网页一致，单页约 50 条），不再截断到 10 条。
                    // 先一次性构造全部行，再用 ReplaceAll 只发一次 Reset 通知——逐条 Add 会触发约 50 次
                    // 列表布局刷新，是战绩页放开全量后 UI 卡顿的主因。
                    var displayItems = battlesResult.Select(BuildBattleDisplayItem).ToList();
                    // 缓存全量后走筛选视图（默认无筛选=显示全部），供下拉级联本地过滤复用同一份数据。
                    _allBattles.Clear();
                    _allBattles.AddRange(displayItems);
                    ApplyBattleFilter();
                    RecentBattlesProgress = 100;
                }
            }
            catch (OperationCanceledException) { }
            catch (NarakaApiException ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplyBattlesAsync)} api error");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplyBattlesAsync)} failed");
            }
            finally
            {
                IsRecentBattlesLoading = false;
            }
        }


       private async System.Threading.Tasks.Task LoadStatsAsync(CancellationToken ct)
        {
            if (_sourceContext == null || string.IsNullOrEmpty(_roleId) || SelectedSeason == null)
                return;

            IsStatsLoading = true;
            StatsProgress = 0;
            try
            {
                var gameMode = GameModeExtensions.FromCategoryAndTeamSize(_selectedCategory, _selectedTeamSize);

                var stats = await _playerStatsLoader.FetchPlayerStatsAsync(
                    _sourceContext, SelectedSeason.Code, gameMode, ct);

                if (stats == null)
                {
                    // stats 为 null 意味着 Loader 层已按静默契约吞掉未知底层异常（无 msg 可展示）；
                    // 若是后端业务错误，NarakaApiException 会冒泡到下面的 catch，那里才是弹 msg 的入口。
                    return;
                }

                // 段位卡：仅当后端给出有效段位（Grade 非空且有段位名/分数）时才显示实际段位；
                // 否则视为该模式未定级——重置为占位，绝不残留上一次模式的段位（切到未定级双排却
                // 还显示三排"蚀月Ⅳ 3600"就是这个 bug）。判定与右侧数据占位分支保持同一口径。
                var hasRank = stats.Grade != null
                    && (!string.IsNullOrEmpty(stats.Grade.GradeName) || stats.Grade.GradeScore > 0);
                if (hasRank)
                {
                    var grade = stats.Grade!;
                    RankName = grade.GradeName;
                    RankIcon = grade.GradeIcon;
                    RankScore = grade.GradeScore;
                    RankLevel = grade.GradeLevel;
                    // 上行段位文字：段位名 + 折算的子段/星数。
                    // - 星阶段位（天玄 >=4500）：名称保持纯段位名，星数由右侧 ⭐图标 + PageStarCount 展示。
                    // - 非星阶排位段：名称后直接拼子段数字（如“坠日4”）。
                    // 段位名优先用后端 rank.name；后端未给（该模式无数据）时才按分数自算。
                    var pageRankBase = !string.IsNullOrEmpty(grade.GradeName)
                        ? grade.GradeName.Trim()
                        : GetRankNameForScore(grade.GradeScore, (int)gameMode);
                    PageStarCount = GetStarCount(grade.GradeScore, (int)gameMode);
                    PageHasStars = IsTianxuanMode((int)gameMode) && grade.GradeScore >= 4500;
                    PageRankName = PageHasStars
                        ? pageRankBase
                        : pageRankBase + GetSubTierName(grade.GradeScore, IsTianxuanMode((int)gameMode));
                    RankTierScore = GetRankTierScore(grade.GradeScore, (int)gameMode);
                }
                else
                {
                    ResetRankCardToUnranked();
                }

                DetailStats.Clear();
                if (stats.Stats != null && stats.Stats.Count > 0)
                {
                    foreach (var s in stats.Stats)
                    {
                        var label = FormatStatLabel(s.Key, s.Name);
                        var value = string.IsNullOrEmpty(s.Value) ? "0" : s.Value;

                        // Convert survival time from seconds to mm:ss format
                        if (s.Key.Contains("live_time", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("生存") || s.Key.Contains("存活时间"))
                        {
                            value = FormatSurvivalTime(value);
                        }

                        DetailStats.Add(new StatEntryItem
                        {
                            Label = label,
                            Value = value
                        });
                    }
                    // Parse specific rank stats from the dynamic list
                    TotalGames = FindStatValue(stats.Stats, "对局", "场次", "game", "battle", "round");
                    TopOneCount = FindStatValue(stats.Stats, "第一", "冠军", "吃鸡", "champion", "top1", "win", "夺冠");
                    TopFiveCount = FindStatValue(stats.Stats, "前五", "top5");
                    AvgDamage = FindStatValue(stats.Stats, "场均伤害", "场均", "伤害", "damage", "avgDamage");
                }
                else
                {
                    TotalGames = "0";
                    TopOneCount = "0";
                    TopFiveCount = "0";
                    AvgDamage = "0";
                    // 未定级/该模式无数据：与网页 core-stats 一致，仍显示固定指标标题、值用 "-" 占位，
                    // 而非整块空白。
                    AddPlaceholderStats();
                }
            }
            catch (OperationCanceledException) { }
            catch (NarakaApiException ex)
            {
                AppLog.Error(ex, "StatsPage", "LoadStatsAsync api error");
                if (!string.IsNullOrEmpty(ex.Msg))
                    _tipMessage.ShowError(ex.Msg!);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "StatsPage", "LoadStatsAsync failed");
            }
            finally
            {
                StatsProgress = 100;
                IsStatsLoading = false;
            }
        }

        
        private string FormatStatLabel(string? key, string? fallbackName)
        {
            if (!string.IsNullOrEmpty(key) && StatKeyToResourceKey.TryGetValue(key, out var resourceKey))
            {
                return _localizedText.Get(resourceKey, fallbackName ?? key);
            }
            // heyBox 分支 key 直接是中文 desc，作为 label 显示即可
            return !string.IsNullOrEmpty(fallbackName) ? fallbackName : (key ?? string.Empty);
        }

        /// <summary>
        /// 未定级/该模式无段位：段位卡重置为占位——清空图标、分数，段位名显示"未定级"，隐藏星标与分数行。
        /// 避免切到未定级模式时残留上一次模式的段位（与右侧数据项 "-" 占位同一语义）。
        /// </summary>
        private void ResetRankCardToUnranked()
        {
            RankName = string.Empty;
            RankIcon = string.Empty;
            RankScore = 0;
            RankLevel = string.Empty;
            PageRankName = _localizedText.Get("Stats.Unranked", "未定级");
            PageStarCount = 0;
            PageHasStars = false;
            RankTierScore = 0;
        }

        /// <summary>
        /// 未定级/无数据时的占位指标：与网页 core-stats 一致，显示固定标题（对局数/前五率/K/D/场伤），
        /// 值统一为 "-"。标题走本地化，与有数据时同一套资源 key。
        /// </summary>
        private void AddPlaceholderStats()
        {
            var placeholders = new[]
            {
                _localizedText.Get("Stats.Matches", "对局数"),
                _localizedText.Get("Stats.TopFiveRate", "前五率"),
                _localizedText.Get("Stats.KD", "K/D"),
                _localizedText.Get("Stats.AvgDamage", "场伤"),
            };
            foreach (var label in placeholders)
                DetailStats.Add(new StatEntryItem { Label = label, Value = "-" });
        }

        private static string FindStatValue(List<UnifiedStatEntry> stats, params string[] keyPatterns)
        {
            foreach (var s in stats)
            {
                var label = string.IsNullOrEmpty(s.Name) ? s.Key : s.Name;
                label = label.ToLowerInvariant();
                foreach (var pattern in keyPatterns)
                {
                    if (label.Contains(pattern.ToLowerInvariant()))
                        return string.IsNullOrEmpty(s.Value) ? "0" : s.Value;
                }
            }
            return "0";
        }

        private string FormatSurvivalTime(string secondsStr)
        {
            if (double.TryParse(secondsStr, out double seconds))
            {
                var minutes = (int)(seconds / 60);
                var remainSeconds = (int)(seconds % 60);
                var minUnit = _localizedText.Get("Stats.Minute", "分");
                var secUnit = _localizedText.Get("Stats.Second", "秒");
                return $"{minutes}{minUnit}{remainSeconds:D2}{secUnit}";
            }
            return secondsStr;
        }

        /// <summary>
        /// 模式名优先取后端 mode.name（与网页一致）；后端未给（dashen 源 mode 为 null）时显示"未知模式"。
        /// </summary>
        private string ResolveModeName(UnifiedRecentBattleItem b)
        {
            if (!string.IsNullOrEmpty(b.ModeName)) return b.ModeName!;
            return _localizedText.Get("GameMode.Unknown", "未知");
        }

        /// <summary>
        /// 模式大类文本：优先用后端 mode.category（rank/match/tianren）本地化；后端未给时回退按 battleApiCode 反推，仍无则"未知模式"。
        /// </summary>
        private string ResolveModeCategoryText(UnifiedRecentBattleItem b)
        {
            var category = ParseCategory(b.ModeCategory);
            if (category.HasValue)
                return _localizedText.Get("GameMode." + category.Value, category.Value.ToString());
            return FormatGameModeCategory(b.GameMode);
        }

        /// <summary>
        /// 队伍人数文本：优先用后端 mode.teamSize（1/2/3）本地化；后端未给（0）时回退按 battleApiCode 反推。
        /// </summary>
        private string ResolveModeTeamSizeText(UnifiedRecentBattleItem b)
        {
            var size = b.ModeTeamSize switch
            {
                1 => (TeamSize?)TeamSize.Solo,
                2 => TeamSize.Duo,
                3 => TeamSize.Trio,
                _ => null,
            };
            if (size.HasValue)
                return _localizedText.Get("GameMode." + size.Value, size.Value.ToString());
            return FormatGameModeTeamSize(b.GameMode);
        }

        private static GameModeCategory? ParseCategory(string? category) => category?.ToLowerInvariant() switch
        {
            "rank" => GameModeCategory.Rank,
            "match" => GameModeCategory.Match,
            "tianren" => GameModeCategory.Tianren,
            "fun" => GameModeCategory.Fun,
            _ => null,
        };

        /// <summary>
        /// 把列表项携带的 gameMode 整数解析为 GameMode 枚举。
        /// 该值可能是 GameMode 枚举原值（如 101），也可能是对局历史 API 编码 battleApiCode
        /// （miniProgram 的 subtype / heyBox 归一化后的值，如 2=RankTrio）。两种都尝试，均不中返回 null。
        /// </summary>
        private static GameMode? TryResolveGameMode(int gameMode)
        {
            if (Enum.IsDefined(typeof(GameMode), gameMode))
                return (GameMode)gameMode;

            try
            {
                return GameModeExtensions.FromBattleApiCode(gameMode);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private string FormatGameModeCategory(int gameMode)
        {
            var gm = TryResolveGameMode(gameMode);
            if (gm.HasValue)
            {
                try
                {
                    var category = gm.Value.GetCategory();
                    var key = "GameMode." + category.ToString();
                    return _localizedText.Get(key, category.ToString());
                }
                catch (ArgumentOutOfRangeException)
                {
                    return _localizedText.Get("GameMode.Unknown", "Unknown");
                }
            }

            return _localizedText.Get("GameMode.Unknown", "Unknown");
        }

        private string FormatGameModeTeamSize(int gameMode)
        {
            var gm = TryResolveGameMode(gameMode);
            if (gm.HasValue)
            {
                try
                {
                    var teamSize = gm.Value.GetTeamSize();
                    var key = "GameMode." + teamSize.ToString();
                    return _localizedText.Get(key, teamSize.ToString());
                }
                catch (ArgumentOutOfRangeException)
                {
                    return string.Empty;
                }
            }

            return _localizedText.Get("GameMode.Unknown", "Unknown");
        }
        private static string FormatUnixTime(long unixMilliseconds)
        {
            try
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).LocalDateTime;
                return dt.ToString("yyyy/MM/dd HH:mm");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(StatsPageViewModel)}.FormatUnixTime", "FormatUnixTime failed");
                return string.Empty;
            }
        }

        private static bool IsTianxuanMode(double gameMode)
        {
            var mode = (int)gameMode;
            // 直接 GameMode 枚举值（如 101=RankSolo）
            if (Enum.IsDefined(typeof(GameMode), mode))
                return ((GameMode)mode).GetCategory() == GameModeCategory.Rank;

            // 通过对局历史 API 编码（如 1=RankSolo）
            try
            {
                var gm = GameModeExtensions.FromBattleApiCode(mode);
                return gm.GetCategory() == GameModeCategory.Rank;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private string GetRankNameForScore(double score, int gameMode = 0)
        {
            if (IsTianxuanMode(gameMode))
            {
                if (score >= 7500) return L("Stats.RankName.Solo.7500", "无量梵天");
                if (score >= 6000) return L("Stats.RankName.Solo.6000", "无相龙王");
                if (score >= 5000) return L("Stats.RankName.Solo.5000", "无双修罗");
                if (score >= 4500) return L("Stats.RankName.Solo.4500", "无间修罗");
                if (score >= 4000) return L("Stats.RankName.Solo.4000", "坠日");
                if (score >= 3500) return L("Stats.RankName.Solo.3500", "蚀月");
                if (score >= 3000) return L("Stats.RankName.Solo.3000", "陨星");
                if (score >= 2500) return L("Stats.RankName.Solo.2500", "铂金");
                if (score >= 2000) return L("Stats.RankName.Solo.2000", "黄金");
                if (score >= 1500) return L("Stats.RankName.Solo.1500", "白银");
                if (score >= 1000) return L("Stats.RankName.Solo.1000", "青铜");
                return string.Empty;
            }
            else
            {
                if (score >= 7000) return L("Stats.RankName.Trio.7000", "无间泰斗");
                if (score >= 6500) return L("Stats.RankName.Trio.6500", "御天尊者");
                if (score >= 6000) return L("Stats.RankName.Trio.6000", "劫虚圣主");
                if (score >= 5500) return L("Stats.RankName.Trio.5500", "穹苍魁首");
                if (score >= 5000) return L("Stats.RankName.Trio.5000", "日曜名宿");
                if (score >= 4500) return L("Stats.RankName.Trio.4500", "星月宗师");
                if (score >= 4000) return L("Stats.RankName.Trio.4000", "云霄武圣");
                if (score >= 3500) return L("Stats.RankName.Trio.3500", "绝顶高手");
                if (score >= 3000) return L("Stats.RankName.Trio.3000", "凡尘武师");
                return L("Stats.RankName.Trio.3000", "凡尘武师");
            }
        }

        private static int GetStarCount(double score, int gameMode = 0)
        {
            if (!IsTianxuanMode(gameMode)) return 0;
            if (score >= 4500) return (int)((score - 4500) / 100); // 修罗以上：星数
            return 0;
        }

        private static string FormatScoreDiff(double diff)
        {
            var sign = diff >= 0 ? "+" : "";
            return "(" + sign + diff + ")";
        }

        private static double GetRankTierScore(double score, int gameMode = 0)
        {
            var isTianxuan = IsTianxuanMode(gameMode);
            if (!isTianxuan) return score;

            if (score >= 4500)
                return (score - 4500) % 100; // 星阶段位内分数

            if (score >= 1000)
            {
                var tierBase = GetTierBase(score, true);
                return (score - tierBase) % 100; // 子段内分数
            }

            return score;
        }

        /// <summary>
        /// 获取段位的起始分数线（该大段的最低分）
        /// </summary>
        private static double GetTierBase(double score, bool isTianxuan)
        {
            if (isTianxuan)
            {
                if (score >= 7500) return 7500;
                if (score >= 6000) return 6000;
                if (score >= 5000) return 5000;
                if (score >= 4500) return 4500;
                if (score >= 4000) return 4000;
                if (score >= 3500) return 3500;
                if (score >= 3000) return 3000;
                if (score >= 2500) return 2500;
                if (score >= 2000) return 2000;
                if (score >= 1500) return 1500;
                if (score >= 1000) return 1000;
                return 0;
            }
            else
            {
                if (score >= 7000) return 7000;
                if (score >= 6500) return 6500;
                if (score >= 6000) return 6000;
                if (score >= 5500) return 5500;
                if (score >= 5000) return 5000;
                if (score >= 4500) return 4500;
                if (score >= 4000) return 4000;
                if (score >= 3500) return 3500;
                if (score >= 3000) return 3000;
                return 0;
            }
        }

        /// <summary>
        /// 获取子段位名称（五、四、三、二、一），每小段 100 分
        /// 仅对排位模式 1000~4499 分有效
        /// </summary>
        private static string GetSubTierName(double score, bool isTianxuan)
        {
            if (!isTianxuan) return string.Empty;
            if (score < 1000 || score >= 4500) return string.Empty;

            var tierBase = GetTierBase(score, isTianxuan);
            var offset = score - tierBase;
            var subTierIndex = (int)(offset / 100);
            var names = new[] { "5", "4", "3", "2", "1" };
            if (subTierIndex >= 0 && subTierIndex < names.Length)
                return names[subTierIndex];
            return string.Empty;
        }

        /// <summary>
        /// 判断是否应该在 DataGrid 行中显示段位分数
        /// </summary>
        private static bool ShouldShowTierScore(double score, int gameMode)
        {
            if (IsTianxuanMode(gameMode))
                return score >= 1500;
            return false;
        }
    }

    public class StatEntryItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class RecentBattleDisplayItem
    {
       public string BattleId { get; set; } = string.Empty;
       public double Rank { get; set; }
       public string HeroIcon { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public string GameModeText { get; set; } = string.Empty;
        public int GameMode { get; set; }
        public string GameModeCategoryText { get; set; } = string.Empty;
        public string GameModeTeamSizeText { get; set; } = string.Empty;
        public int Kill { get; set; }
        public int Damage { get; set; }
        public double ScoreNumber { get; set; }
        public double ScoreDiff { get; set; }
        public string RankDisplayText { get; set; } = string.Empty;
        public bool ShowScoreNumber { get; set; }
        public double StarCount { get; set; }
        public bool HasStars { get; set; }
        public string ScoreDiffDisplay { get; set; } = string.Empty;
        public string BattleTime { get; set; } = string.Empty;
        public bool IsRankMode { get; set; }
        public string Rating { get; set; } = string.Empty;
   }

}
