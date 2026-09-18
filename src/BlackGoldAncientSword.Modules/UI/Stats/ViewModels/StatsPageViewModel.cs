using System.Collections.ObjectModel;
using System.Globalization;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
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
        private const int FilterRefreshDebounceMs = 1000;

        private readonly IPlayerPrefsService _playerPrefsService;
        private readonly ITipMessageService _tipMessage;
        private readonly ILocalizationService _localizationService;
        private readonly IClipboardService _clipboard;
        private readonly PlayerStatsLoader _playerStatsLoader;
        private readonly BattleListLoader _battleListLoader;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly HeyboxRequestCache _requestCache;
        private readonly PropertyChangedEventHandler? _onLanguageChangedHandler;
        private readonly TrailingDebouncer _filterRefreshDebouncer;
        private CancellationTokenSource? _loadAllCts;
        private CancellationTokenSource? _loadStatsCts;
        private bool _lastLoadSucceeded;
        private PrefetchedPlayer? _prefetchedPlayer;

        /// <summary>最近一次拿到的近期场均名次，语言切换时据此重算展示文案。</summary>
        private double _recentAvgRank;

        public StatsPageViewModel(
            IPlayerPrefsService playerPrefsService,
            ILocalizationService localizationService,
            ITipMessageService tipMessageService,
            IClipboardService clipboard,
            PlayerStatsLoader playerStatsLoader,
            BattleListLoader battleListLoader,
            IUIDispatcher uiDispatcher,
            ILocalizedTextProvider localizedText,
            HeyboxRequestCache requestCache,
            PlayerSearchSuggester searchSuggester)
        {
            _playerPrefsService = playerPrefsService;
            _tipMessage = tipMessageService;
            _localizationService = localizationService;
            _clipboard = clipboard;
            _playerStatsLoader = playerStatsLoader;
            _battleListLoader = battleListLoader;
            _uiDispatcher = uiDispatcher;
            _localizedText = localizedText;
            _requestCache = requestCache;
            _onLanguageChangedHandler = OnLanguageChanged;
            _filterRefreshDebouncer = new TrailingDebouncer(FilterRefreshDebounceMs, RunFilterRefreshAsync);
            Suggestions = new PlayerSearchSuggestionsViewModel(uiDispatcher, searchSuggester.FetchAsync);
            _suggestionDebouncer = new TrailingDebouncer(SuggestionDebounceMs, RunSuggestionSearchAsync);
            _localizationService.PropertyChanged += _onLanguageChangedHandler;
            Seasons = new ObservableCollection<UnifiedSeason>();
            DetailStats = new ObservableCollection<StatEntryItem>();
            RecentBattles = new RangeObservableCollection<RecentBattleDisplayItem>();
            RefreshStaticLabels();
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
                if (_suppressSuggestionSearch) return;
                _pendingSuggestionKeyword = value;
                _suggestionDebouncer.Trigger();
            }
        }

        /// <summary>
        /// 程序回填输入框（回到我 / 队友卡跳转 / 选中候选）：只更新展示，不顺带弹出候选下拉。
        /// 只有用户自己敲进来的内容才该触发候选搜索。
        /// </summary>
        private void SetSearchTextSilently(string value)
        {
            _suppressSuggestionSearch = true;
            try
            {
                SearchText = value;
            }
            finally
            {
                _suppressSuggestionSearch = false;
            }
        }

        private readonly SearchDebounceGate _searchDebounce = new(SearchDebounceGate.SearchBoxIntervalMilliseconds);

        // 候选下拉：输入停顿 SuggestionDebounceMs 后才去拉第一页。尾沿防抖保证"连续打字只在停下时发一次"，
        // 同一关键词再被搜到时由请求缓存兜住，不会重复打接口。
        private const int SuggestionDebounceMs = 500;

        private readonly TrailingDebouncer _suggestionDebouncer;
        private string _pendingSuggestionKeyword = string.Empty;
        // 确认候选时会回填输入框，那次回填不该再触发一轮候选搜索。
        private bool _suppressSuggestionSearch;

        /// <summary>搜索框候选列表：模糊匹配出的候选 + 滚动到底翻页。</summary>
        public PlayerSearchSuggestionsViewModel Suggestions { get; }

        private System.Threading.Tasks.Task RunSuggestionSearchAsync(CancellationToken debounceCt)
            => Suggestions.RestartAsync(_pendingSuggestionKeyword);

        /// <summary>当前该被确认的候选：优先高亮项，没有高亮时退到第一条。</summary>
        public PlayerSearchSuggestionItem? CurrentSuggestion
        {
            get
            {
                if (!Suggestions.IsOpen) return null;
                var items = Suggestions.Items;
                if (items.Count == 0) return null;
                var index = Suggestions.SelectedIndex;
                if (index < 0 || index >= items.Count) index = 0;
                return items[index];
            }
        }

        /// <summary>输入框里按上下键时在候选里移动高亮（焦点留在输入框，方便接着打字）。</summary>
        public void MoveSuggestionSelection(int delta)
        {
            if (!Suggestions.IsOpen || delta == 0) return;
            var count = Suggestions.Items.Count;
            if (count == 0) return;

            var index = Suggestions.SelectedIndex + delta;
            if (index < 0) index = 0;
            if (index >= count) index = count - 1;
            Suggestions.SelectedIndex = index;
        }

        public void CloseSuggestions() => Suggestions.Close();

        /// <summary>候选列表滚到接近底部时追加下一页（由 View 的滚动事件调用）。</summary>
        public System.Threading.Tasks.Task LoadMoreSuggestionsAsync() => Suggestions.LoadMoreAsync();

        private DelegateCommand<PlayerSearchSuggestionItem>? _selectSuggestionCommand;
        public DelegateCommand<PlayerSearchSuggestionItem> SelectSuggestionCommand =>
            _selectSuggestionCommand ??= new DelegateCommand<PlayerSearchSuggestionItem>(
                item => _ = SelectSuggestionAsync(item));

        /// <summary>
        /// 确认某个候选：候选自带角色 ID 与服务器，直接按它取数据，省掉"再按昵称搜一次"。
        /// 资料取候选里已有的那份，赛季与对局照常拉取（与队伍卡片跳转同一条快照路径）。
        /// </summary>
        public async System.Threading.Tasks.Task SelectSuggestionAsync(PlayerSearchSuggestionItem? item)
        {
            if (item is null || string.IsNullOrEmpty(item.RoleId)) return;

            // 取消还在等防抖的那次候选搜索（回填本身也不再触发新一轮）。
            _suggestionDebouncer.Dispose();
            var displayName = item.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                SetSearchTextSilently(displayName);
                _playerPrefsService.Current.PlayerName = displayName;
            }
            Suggestions.Close();

            _prefetchedPlayer = new PrefetchedPlayer(
                item.RoleId,
                item.Server,
                item.AvatarUrl,
                // 候选里没有等级，只有段位；等级空着即可（段位由随后的统计查询回填）。
                Level: null,
                Seasons: null,
                SeasonKey: null);

            await RefreshAllAsync();
        }

        private DelegateCommand? _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(async () =>
            {
                // 候选下拉开着：回车 / 点放大镜就是确认当前候选，不再按昵称重复搜一次。
                var suggestion = CurrentSuggestion;
                if (suggestion is not null)
                {
                    await SelectSuggestionAsync(suggestion);
                    return;
                }

                if (string.IsNullOrWhiteSpace(SearchText)) return;
                if (!_searchDebounce.TryEnter())
                {
                    _tipMessage.ShowError(L("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }
                _playerPrefsService.Current.PlayerName = SearchText.Trim();
                _requestCache.Invalidate();
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
                // UID 优先分支），所以这里只改查询身份、不动 UID；搜索框回填的是给人看的昵称，
                // 与队友页本地卡（MarkLocalUserSlot 回写 OriginalPlayerName）口径一致。
                _playerPrefsService.Current.PlayerName = _playerPrefsService.Current.OriginalPlayerName;
                SetSearchTextSilently(_playerPrefsService.Current.OriginalPlayerName);
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

        /// <summary>
        /// 段位卡是否显示积分行。未定级（该模式无段位）时为 false——避免显示"0 分"这种假数据。
        /// </summary>
        private bool _showRankScore;
        public bool ShowRankScore
        {
            get => _showRankScore;
            set
            {
                if (_showRankScore == value) return;
                _showRankScore = value;
                RaisePropertyChanged(nameof(ShowRankScore));
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


        // === 近期名次（recent_ranks / recent_avg_rank）===

        private bool _hasRecentRanks;
        public bool HasRecentRanks
        {
            get => _hasRecentRanks;
            set
            {
                if (_hasRecentRanks == value) return;
                _hasRecentRanks = value;
                RaisePropertyChanged(nameof(HasRecentRanks));
            }
        }

        private string _recentAvgRankDisplay = string.Empty;
        /// <summary>"场均排名 13.4"。服务端没给时为空串（整块隐藏）。</summary>
        public string RecentAvgRankDisplay
        {
            get => _recentAvgRankDisplay;
            set
            {
                if (_recentAvgRankDisplay == value) return;
                _recentAvgRankDisplay = value;
                RaisePropertyChanged(nameof(RecentAvgRankDisplay));
            }
        }

        /// <summary>近期每局名次色块（rank==1 红、≤5 粉、其余灰，颜色由 XAML 触发器按 Rank 判定）。</summary>
        public ObservableCollection<RecentRankDisplayItem> RecentRankBlocks { get; } = new();

        /// <summary>
        /// 点名次色块：定位到下方对局列表里的同一局，返回该行交给 View 滚动并选中；找不到返回 null。
        /// 色块与对局列表是两套筛选口径，目标局不一定落在当前视图里：先按当前视图找，找不到就放开本地
        /// 模式筛选再找一次，仍找不到就弹提示——静默无反应会被读成"点了没反应"。
        /// </summary>
        public RecentBattleDisplayItem? ResolveBattleForRankBlock(RecentRankDisplayItem? block)
        {
            var matchId = block?.MatchId ?? string.Empty;
            var target = FindVisibleBattle(matchId);

            // 只有确实带了标识、且当前挂着筛选时才放开筛选重找一次，免得白白清掉用户的筛选状态。
            if (target == null && matchId.Length > 0 && HasBattleFilter)
            {
                ResetBattleFilterCommand.Execute();
                target = FindVisibleBattle(matchId);
            }

            if (target == null)
                _tipMessage.ShowInfo(L("Stats.BattleNotInList", "该对局不在下方列表中"));

            return target;
        }

        /// <summary>下方对局列表当前是否挂着模式/排数筛选。</summary>
        private bool HasBattleFilter => _selectedBattleCategory != null || _selectedBattleTeamSize != null;

        private RecentBattleDisplayItem? FindVisibleBattle(string battleId)
            => RecentBattles.FirstOrDefault(b => b.BattleId == battleId);

        private string _mapColumnHeader = string.Empty;
        /// <summary>对局列表"地图"列表头文案（本地化）。</summary>
        public string MapColumnHeader
        {
            get => _mapColumnHeader;
            set
            {
                if (_mapColumnHeader == value) return;
                _mapColumnHeader = value;
                RaisePropertyChanged(nameof(MapColumnHeader));
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
                _filterRefreshDebouncer.Trigger();
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
                _filterRefreshDebouncer.Trigger();
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
                _filterRefreshDebouncer.Trigger();
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
                    { "Server", _sourceContext?.Server ?? string.Empty },
                    { "DataSource", (int)(_sourceContext?.Source ?? DataSource.HeyBox) },
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

        /// <summary>上一次真正加载过的角色 ID：用来判断"换人了没有"，决定要不要先清掉旧结果块。</summary>
        private string _loadedRoleId = string.Empty;

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
            {
                // 排数/大类选项文案由 SeasonFilterBar 控件自行 ResetBindings；此处只刷新本页专有的对局筛选文案。
                RaisePropertyChanged(nameof(BattleFilterDisplayText));
                // 本页新增的静态标签（能力评分/地图表头/场均排名）随语言切换刷新。
                RefreshStaticLabels();
            }
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);

            // 队友卡片点"查看战绩"时通过导航参数携带目标玩家名。此时必须查该玩家，
            // 不能 reload player_prefs（否则 Current.PlayerName 会被重置回本地账号，串到自己）。
            var targetPlayer = navigationContext.Parameters
                .GetValue<string>(NavigationParameterKeys.TargetPlayerName);

            // 队伍卡片还会把它已经拿到的数据一起带过来（role_id / server / 头像 / 等级 / 赛季列表）。
            // 有这份快照就跳过搜索与资料请求，直接渲染。
            var prefetchedRoleId = navigationContext.Parameters
                .GetValue<string>(NavigationParameterKeys.TargetRoleId);
            _prefetchedPlayer = string.IsNullOrWhiteSpace(prefetchedRoleId)
                ? null
                : new PrefetchedPlayer(
                    prefetchedRoleId!,
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetServer) ?? string.Empty,
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetAvatar),
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetLevel),
                    navigationContext.Parameters.GetValue<IReadOnlyList<UnifiedSeason>>(NavigationParameterKeys.TargetSeasons),
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetSeasonKey));

            // 基类签名为 void，无法 await——把"确定目标玩家 + 刷新 UI + 拉战绩"整块塞进
            // fire-and-forget async。RefreshAllAsync 内部已有 try/catch 兜底。
            _ = LoadForTargetAndRefreshAsync(targetPlayer);
        }

        /// <summary>队伍卡片带过来的数据快照（用一次即丢）。</summary>
        private sealed record PrefetchedPlayer(
            string RoleId,
            string Server,
            string? Avatar,
            string? Level,
            IReadOnlyList<UnifiedSeason>? Seasons,
            string? SeasonKey);

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

                // 页面上正看着别人（搜出来的、队友卡跳过来的）时，重新进入本页不该把浏览对象换回自己。
                // 上面这次 reload 会把 Current.PlayerName 重读成本地账号，若照抄到视图状态并接着查询，
                // 结果就是"搜了别人、切走再回来又变回自己的数据"。本地账号信息已经刷新过了（回到我可用），
                // 这里只是保持页面不动。
                if (IsShowingOtherPlayer())
                {
                    RaisePropertyChanged(nameof(IsLocalUser));
                    return;
                }
            }

            RaisePropertyChanged(nameof(IsLocalUser));
            // 搜索框回填本次查询者：本地用户（targetPlayer 为空）回填本地昵称，队友跳转回填队友名。
            // 两条分支的 PlayerName 都已是目标值（上面刚赋值或刚 reload），直接用即可。
            // 回填只影响展示——查本地用户时 LoadAllAsync 仍按 PlayerId 走 UID 优先分支。
            SetSearchTextSilently(_playerPrefsService.Current.PlayerName);

            // 页面上已经在展示这个玩家的数据 → 直接复用，不再请求。切页回来（同一个玩家）零请求，
            // 只有点搜索图标、或者这一局打完（缓存由"对局结束"事件整体失效）才会重新查。
            if (IsAlreadyShowingTarget(targetPlayer)) return;

            await RefreshAllAsync();
        }

        /// <summary>
        /// 当前页面上展示的是不是即将查询的这个玩家：本地用户按 role_id 精确比对，
        /// 队友跳转按昵称比对。上一次加载失败时一律返回 false，让重进页面能重试。
        /// </summary>
        private bool IsAlreadyShowingTarget(string? targetPlayer)
        {
            if (!_lastLoadSucceeded || string.IsNullOrEmpty(_roleId)) return false;

            if (string.IsNullOrWhiteSpace(targetPlayer))
            {
                return !string.IsNullOrEmpty(_playerPrefsService.Current.PlayerId)
                       && string.Equals(_roleId, _playerPrefsService.Current.PlayerId, StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrEmpty(UserName)
                   && string.Equals(UserName, targetPlayer.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 页面上是不是正展示着一个非本地玩家。本地账号的 role_id 未知时一律返回 false——
        /// 宁可走原来的"重查本地账号"路径，也不要凭猜测把页面钉死在一个不知道是谁的状态上。
        /// </summary>
        private bool IsShowingOtherPlayer()
            => _lastLoadSucceeded
               && !string.IsNullOrEmpty(UserName)
               && !string.IsNullOrEmpty(_roleId)
               && !string.IsNullOrEmpty(_playerPrefsService.Current.PlayerId)
               && !string.Equals(_roleId, _playerPrefsService.Current.PlayerId, StringComparison.OrdinalIgnoreCase);

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            // 语言事件订阅在 ctor 中绑定、在 Dispose 中解绑——单例 VM 跨多次导航复用同一实例，
            // 不能在此处解绑：原实现首次离开页面后再回来时，本地化资源切换不会再触发统计标签刷新。
            CancelAndDispose(ref _loadAllCts);
            CancelAndDispose(ref _loadStatsCts);
            ClearImageBindings();
            // 候选下拉是独立浮层，页面离开后不会自己消失，必须显式收起。
            Suggestions.Close();
            base.OnNavigatedFromExecute(navigationContext);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_localizationService != null && _onLanguageChangedHandler != null)
                    _localizationService.PropertyChanged -= _onLanguageChangedHandler;
                _filterRefreshDebouncer.Dispose();
                _suggestionDebouncer.Dispose();
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
                // 段位列直接展示官方 rating 原值（如 3629）。不再折算"段位内余数"——
                // 那是客户端自造口径，与网页端显示的数对不上。
                ScoreNumber = b.RoundRankScore,
                // 分差直接采用小黑盒算好的 rating_delta，不再客户端 end-begin 相减。
                ScoreDiff = b.ScoreDelta,
                RankDisplayText = GetRankNameForScore(b.RoundRankScore, modeCode) + GetSubTierName(b.RoundRankScore, IsTianxuanMode(modeCode)),
                ShowScoreNumber = ShouldShowRankScore(b.RoundRankScore, modeCode),
                StarCount = GetStarCount(b.RoundRankScore, modeCode),
                HasStars = IsTianxuanMode(modeCode) && b.RoundRankScore >= 4500,
                ScoreDiffDisplay = b.ScoreDelta == 0 && b.BeginRankScore == null
                    ? string.Empty
                    : FormatScoreDiff(b.ScoreDelta),
                BattleTime = FormatUnixTime(b.BattleEndTimeMs),
                Rating = b.Rating ?? string.Empty,
                // 地图名取 map_name 原值；scene 是官方残缺映射，不能当地图名用。
                MapName = string.IsNullOrEmpty(b.MapName)
                    ? _localizedText.Get("GameMode.Unknown", "未知")
                    : b.MapName,
                // 注：play_num（自己队伍人数，三排=3）不单独展示——它与排数列由 mode.teamSize 得出的
                // "三排/双排/单排"是同一个信息（实测 20 条三排对局 play_num 恒为 3），拼在一起只会重复。
            };
        }

        private async void RefreshStats()
        {
            CancelAndDispose(ref _loadStatsCts);
            _loadStatsCts = new CancellationTokenSource();
            await LoadStatsAsync(_loadStatsCts.Token);
        }

        private async System.Threading.Tasks.Task RunFilterRefreshAsync(CancellationToken debounceCt)
        {
            if (debounceCt.IsCancellationRequested) return;
            await _uiDispatcher.InvokeAsync(() => RefreshStats()).ConfigureAwait(false);
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
            ShowRankScore = false;
            ClearRecentRanks();
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
        /// 换到另一个玩家时先清掉上一个玩家的结果块（赛季 / 段位卡 / 统计 / 名次色块 / 对局列表）。
        /// <para>
        /// 这几块是各自独立加载、各自吞异常的：任何一块被限流或超时没回来，它就不会被覆盖，
        /// 旧玩家的数据会一直挂在页面上——表现就是"顶部已经是新玩家的昵称和 UID，下面还是上一个人的数据"。
        /// 宁可空着（各自有 loading / 占位），也不能把别人的数据挂在这个人的名下。
        /// </para>
        /// <para>同一个玩家重复查询不动它：数据本来就是他的，清一遍只会白白闪一下。</para>
        /// </summary>
        private void ResetResultBlocksFor(string roleId)
        {
            if (string.Equals(_loadedRoleId, roleId, StringComparison.OrdinalIgnoreCase)) return;
            _loadedRoleId = roleId;

            Seasons.Clear();
            _selectedSeason = null;
            RaisePropertyChanged(nameof(SelectedSeason));

            ClearRecentRanks();
            DetailStats.Clear();
            ResetRankCardToUnranked();

            _selectedBattleCategory = null;
            _selectedBattleTeamSize = null;
            _allBattles.Clear();
            RecentBattles.Clear();
            HasNoBattleResult = false;
            IsBattleFilterOpen = false;
            RaisePropertyChanged(nameof(SelectedBattleCategory));
            RaisePropertyChanged(nameof(SelectedBattleTeamSize));
            RaisePropertyChanged(nameof(BattleFilterDisplayText));
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
            _lastLoadSucceeded = success;

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
                // 队伍卡片带了快照：直接渲染，不搜索、也不请求资料与赛季。
                // 只剩对局列表要取——那是卡片从来没有的数据。
                if (_prefetchedPlayer is { } prefetched)
                {
                    _prefetchedPlayer = null;
                    _roleId = prefetched.RoleId;
                    _sourceContext = new PlayerSourceContext(prefetched.RoleId, prefetched.Server);
                    ResetResultBlocksFor(prefetched.RoleId);
                    var prefetchedCtx = _sourceContext;
                    var prefetchedSeasons = prefetched.Seasons;

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        UserName = localName;
                        UID = prefetched.RoleId;
                        AvatarUrl = prefetched.Avatar ?? string.Empty;
                        Level = prefetched.Level ?? string.Empty;
                        IsPlayerInfoLoading = false;

                        if (prefetchedSeasons is not { Count: > 0 }) return;

                        Seasons.Clear();
                        foreach (var season in prefetchedSeasons) Seasons.Add(season);

                        var target = Seasons.FirstOrDefault(s => s.SeasonKey == prefetched.SeasonKey) ?? Seasons[0];
                        if (Equals(SelectedSeason, target)) RefreshStats();
                        else SelectedSeason = target;
                    }).ConfigureAwait(false);

                    // 没带赛季列表（比如队伍页还没加载完）才退回原路径去取。
                    if (prefetchedSeasons is not { Count: > 0 })
                        await ApplySeasonsAsync(prefetchedCtx, ct).ConfigureAwait(false);

                    await ApplyBattlesAsync(prefetchedCtx, ct).ConfigureAwait(false);
                    return true;
                }

                // 查的是本地用户时，用本地 role_id（player_prefs 的 player_id）在搜索结果里**精确挑人**：
                // role_id 唯一，昵称可能重名。判断"是否本地用户"用 PlayerName == OriginalPlayerName。
                // 注意 role_id 不能当搜索关键词——搜索接口只吃昵称，传 role_id 会静默返回空列表。
                var localRoleId =
                    !string.IsNullOrEmpty(_playerPrefsService.Current.OriginalPlayerName)
                    && string.Equals(localName, _playerPrefsService.Current.OriginalPlayerName, StringComparison.OrdinalIgnoreCase)
                        ? _playerPrefsService.Current.PlayerId
                        : null;

                var search = await _playerStatsLoader.SearchLocalPlayerAsync(localRoleId, localName, ct);
                if (search == null || string.IsNullOrEmpty(search.RoleIdSimple))
                {
                    ShowNotFound = true;
                    ClearAllData();
                    _tipMessage.ShowError(L("Stats.PlayerNotFound", "未找到该玩家，请检查名称是否正确"));
                    return false;
                }
                _roleId = search.RoleIdSimple;
                _sourceContext = new PlayerSourceContext(_roleId, search.Server);
                ResetResultBlocksFor(_roleId);
                var ctx = _sourceContext;

                // 三块数据（玩家信息 / 赛季 / 对局列表）彼此无依赖：并行发起，且各自完成即绑定自己的 UI，
                // 不再 WhenAll 干等最慢的一路。原实现等三者全回来才统一绑定，最慢的 battles（实测约 5s）
                // 把 userInfo（约 1s）也拖成 5s 才显示——用户整页转圈无法操作。拆开后玩家信息一秒即出，
                // 对局列表区自己转圈，谁快谁先亮。三个 Apply* 各自 try/catch + 关自己的 loading，互不影响。
                var userInfoApply = ApplyUserInfoAsync(ctx, localName, ct);
                var seasonsApply = ApplySeasonsAsync(ctx, ct);
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
                    // 官方口径是大写 "LV."。服务端没下发 lv 时解析成 0，此时不显示——
                    // 显示「LV.0」比不显示更像故障。
                    Level = userInfo.RoleLevel > 0 ? $"LV.{(int)userInfo.RoleLevel}" : string.Empty;
                    UID = userInfo.Uid;
                    AvatarUrl = userInfo.HeadIcon;
                }
                else
                {
                    // 资料没回来：昵称用本次查询的目标名（搜索阶段已经确认过是这个人），
                    // 但 UID / 头像 / 等级必须清掉——留着就是上一个玩家的。
                    UserName = localName;
                    Level = string.Empty;
                    UID = string.Empty;
                    AvatarUrl = string.Empty;
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
        private async System.Threading.Tasks.Task ApplySeasonsAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            try
            {
                var (seasons, currentSeasonKey) = await _playerStatsLoader.FetchSeasonsAsync(ctx, ct);
                ct.ThrowIfCancellationRequested();
                Seasons.Clear();
                foreach (var s in seasons) Seasons.Add(s);

                if (Seasons.Count > 0)
                {
                    // 默认选中**服务端回显的当前赛季**，而不是写死第一项：第一项是「全部」（pre-01），
                    // 而服务端在不传 season 时用的是当前赛季——选中项与真实数据对不上。
                    var target = Seasons.FirstOrDefault(s => s.SeasonKey == currentSeasonKey) ?? Seasons[0];

                    // SelectedSeason 赋值会触发 RefreshStats → LoadStatsAsync，由其 finally 关闭 IsStatsLoading。
                    // 但重复查同一玩家时新旧值可能相等（setter 短路不触发 RefreshStats），会让 IsStatsLoading
                    // 悬空转圈——此时显式刷新一次统计。
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

        /// <summary>对局列表：拉取后一次性构造全部行并 ReplaceAll（仅一次 Reset 通知），与其它两块互不阻塞。
        /// 行的成就图标不在此处同步填充（50 行 × 多图标同步建 BitmapImage 是首屏卡顿主因），
        /// 由 <see cref="ApplyHonorTitlesAsync"/> 分批后台填充。</summary>
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
                    // 行已先渲染：成就图标后台分批填充，每批让出 UI 线程，避免一次塞入大量图片卡顿。
                    _ = ApplyHonorTitlesAsync(battlesResult, displayItems, ct);
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

        /// <summary>
        /// 把接口返回的成就分批填充到已渲染的行上。行数据已经显示，成就只是"锦上添花"，
        /// 不应拖慢首屏：每次仅处理若干行、每批之间让出 UI 线程让界面先绘制。
        /// <paramref name="sourceItems"/> 与 <paramref name="displayItems"/> 索引一一对应。
        /// </summary>
        private async System.Threading.Tasks.Task ApplyHonorTitlesAsync(
            System.Collections.Generic.List<UnifiedRecentBattleItem> sourceItems,
            System.Collections.Generic.List<RecentBattleDisplayItem> displayItems,
            CancellationToken ct)
        {
            // 每批行数：一次填充过多会造成单帧 Image 创建过多；太小则填充耗时。
            const int batchSize = 5;
            const int batchDelayMs = 8;
            try
            {
                for (int i = 0; i < sourceItems.Count; i += batchSize)
                {
                    if (ct.IsCancellationRequested) return;
                    var end = Math.Min(i + batchSize, sourceItems.Count);

                    // 在 UI 线程填充本批成就：ObservableCollection.Add 会触发绑定更新，不能跨线程。
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        for (int j = i; j < end; j++)
                        {
                            var source = sourceItems[j];
                            if (source.HonorTitles == null || source.HonorTitles.Count == 0)
                                continue;
                            var row = displayItems[j];
                            foreach (var h in source.HonorTitles)
                            {
                                row.HonorTitles.Add(new HonorTitleDisplayItem
                                {
                                    Icon = h.Icon,
                                    Name = h.Name,
                                    Desc = h.Desc,
                                });
                            }
                        }
                    }).ConfigureAwait(false);

                    // 每批之间让出 UI 线程，让已填充的图标先绘制，避免整页一次卡死。
                    await System.Threading.Tasks.Task.Delay(batchDelayMs, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.Error(ex, "StatsPage", $"{nameof(ApplyHonorTitlesAsync)} failed");
            }
        }


       private async System.Threading.Tasks.Task LoadStatsAsync(CancellationToken ct)
        {
            if (_sourceContext == null || string.IsNullOrEmpty(_roleId) || SelectedSeason == null)
            {
                // 没有可查的目标（换玩家的空窗期、或没有赛季可选）。必须顺手关掉转圈：
                // 上游 RefreshAllAsync / 筛选变更已经置过 IsStatsLoading=true，直接 return 会让统计区永久转圈。
                IsStatsLoading = false;
                return;
            }

            IsStatsLoading = true;
            StatsProgress = 0;
            try
            {
                var gameMode = GameModeExtensions.FromCategoryAndTeamSize(_selectedCategory, _selectedTeamSize);

                var stats = await _playerStatsLoader.FetchPlayerStatsAsync(
                    _sourceContext, SelectedSeason.SeasonKey, gameMode, ct);

                if (stats == null)
                {
                    // stats 为 null 意味着 Loader 层已按静默契约吞掉未知底层异常（无 msg 可展示）；
                    // 若是后端业务错误，NarakaApiException 会冒泡到下面的 catch，那里才是弹 msg 的入口。
                    // 清掉依赖本次响应的块，避免残留上一次模式的数据。
                    ClearRecentRanks();
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
                    // RankScore 就是官方唯一的那个数（player_info.rating，如 3629），段位卡直接展示它。
                    RankScore = grade.GradeScore;
                    RankLevel = grade.GradeLevel;
                    // 上行段位文字：服务端给的段位名（player_info.level）**已经含罗马数字子段**（"蚀月Ⅳ"），
                    // 只有服务端没给名字时才回退到按分自算，避免拼成"蚀月Ⅳ4"。
                    // - 星阶段位（天玄 >=4500）：名称保持纯段位名，星数由右侧 ⭐图标 + PageStarCount 展示。
                    // - 非星阶排位段且名字是自算的：名称后补子段数字（如"坠日4"）。
                    var serverGradeName = grade.GradeName?.Trim() ?? string.Empty;
                    var hasServerGradeName = !string.IsNullOrEmpty(serverGradeName);
                    var pageRankBase = hasServerGradeName
                        ? serverGradeName
                        : GetRankNameForScore(grade.GradeScore, (int)gameMode);
                    PageStarCount = GetStarCount(grade.GradeScore, (int)gameMode);
                    PageHasStars = IsTianxuanMode((int)gameMode) && grade.GradeScore >= 4500;
                    PageRankName = hasServerGradeName || PageHasStars
                        ? pageRankBase
                        : pageRankBase + GetSubTierName(grade.GradeScore, IsTianxuanMode((int)gameMode));
                    ShowRankScore = grade.GradeScore > 0;
                }
                else
                {
                    ResetRankCardToUnranked();
                }

                ApplyRecentRanks(stats);

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
                            Value = value,
                            // overview[] 的 S/A 角标：空串=服务端没给评级，UI 不渲染。
                            Grade = s.Grade,
                            HasGrade = !string.IsNullOrEmpty(s.Grade),
                        });
                    }
                }
                else
                {
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

        
        /// <summary>近期名次块：平均名次文案 + 每局名次色块（服务端未返回时整块隐藏）。</summary>
        private void ApplyRecentRanks(UnifiedPlayerStats stats)
        {
            _recentAvgRank = stats.RecentAvgRank;
            RecentRankBlocks.Clear();
            foreach (var r in stats.RecentRanks)
                RecentRankBlocks.Add(new RecentRankDisplayItem { MatchId = r.MatchId, Rank = r.Rank, Display = FormatRankPosition(r.Rank) });

            HasRecentRanks = RecentRankBlocks.Count > 0 || _recentAvgRank > 0;
            RecentAvgRankDisplay = _recentAvgRank > 0
                ? $"{L("Stats.RecentAvgRank", "场均排名")} {FormatScoreValue(_recentAvgRank)}"
                : string.Empty;
        }

        /// <summary>名次色块提示文案（"第 13 名 · 点击定位对局"）。</summary>
        private string FormatRankPosition(int rank)
            => string.Format(CultureInfo.InvariantCulture, L("Stats.RankPosition", "第 {0} 名 · 点击定位对局"), rank);

        /// <summary>整数不带小数点，小数保留一位（"96.7"）。服务端下发的是展示用原文，不折算单位。</summary>
        private static string FormatScoreValue(double value)
            => value == Math.Floor(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>页面上的静态标签（表头/场均排名）——语言切换或初始化时刷新。</summary>
        private void RefreshStaticLabels()
        {
            MapColumnHeader = L("Stats.Header.Map", "地图");
            if (_recentAvgRank > 0)
                // 名次色块与「场均排名」都来自 home/data 的 recent_ranks/recent_avg_rank，
                // **随赛季/模式筛选变化**；而下面的对局列表来自 match/list（不传筛选参数）。
                // 两块紧挨着竖排，必须把口径写出来，否则切筛选时"色块换了、列表没换"会被读成同一批。
                RecentAvgRankDisplay =
                    $"{L("Stats.RecentRanksScope", "近 20 局（当前模式）")} · " +
                    $"{L("Stats.RecentAvgRank", "场均排名")} {FormatScoreValue(_recentAvgRank)}";
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
            // 未定级没有积分可言，隐藏积分行（保留 RankScore=0 供其它逻辑复用）。
            ShowRankScore = false;
        }

        /// <summary>清空近期名次块（recent_ranks 缺席时整块隐藏）。</summary>
        private void ClearRecentRanks()
        {
            HasRecentRanks = false;
            RecentAvgRankDisplay = string.Empty;
            RecentRankBlocks.Clear();
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
        /// 模式名。小黑盒的对局项不带模式名，但带 <c>battle_tid</c>；由它反查出 GameMode 再取本地化名。
        /// 两者都没有（无法归类的对局）时显示"未知模式"。
        /// </summary>
        private string ResolveModeName(UnifiedRecentBattleItem b)
        {
            if (!string.IsNullOrEmpty(b.ModeName)) return b.ModeName!;

            var gm = TryResolveGameMode(b.GameMode);
            if (gm != null)
                return _localizedText.Get("GameMode." + gm.Value, gm.Value.ToString());

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
            // 服务端漏发 time 时下游给的是 0；FromUnixTimeMilliseconds(0) 会格式化成
            // "1970/01/01 08:00" 这种假时间，宁可留空也不要显示它。
            if (unixMilliseconds <= 0) return string.Empty;

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

        /// <summary>
        /// 获取段位的起始分数线（该大段的最低分）。段位名/子段名的客户端兜底计算用。
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
            // 用罗马数字：小黑盒 player_info.level 就是「蚀月Ⅳ」这种形态（子段用罗马数字）。
            // 自算路径只有在服务端没下发 level 时才会走到，但两处必须同一口径，
            // 否则同一分段位卡显示「蚀月Ⅳ」、对局列表显示「蚀月4」，用户会以为是两个不同的分。
            var names = new[] { "Ⅴ", "Ⅳ", "Ⅲ", "Ⅱ", "Ⅰ" };
            if (subTierIndex >= 0 && subTierIndex < names.Length)
                return names[subTierIndex];
            return string.Empty;
        }

        /// <summary>
        /// 判断是否应该在 DataGrid 行中显示段位分。
        /// 现在展示的是官方 rating 原值，所以门槛就是"该模式有段位分"（&gt; 0），
        /// 不再需要原先为折算余数而设的 1500 分门槛。
        /// </summary>
        private static bool ShouldShowRankScore(double score, int gameMode)
        {
            if (!IsTianxuanMode(gameMode)) return false;
            return score > 0;
        }
    }

    public class StatEntryItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        /// <summary>服务端下发的指标评级角标（S/A/B/C/D）。空串=没给评级。</summary>
        public string Grade { get; set; } = string.Empty;
        /// <summary>是否有评级角标。空串不渲染占位。</summary>
        public bool HasGrade { get; set; }
    }

    /// <summary>近期单局名次色块（rank==1 红、≤5 粉、其余灰，由 XAML 触发器按 Rank 判定）。</summary>
    public class RecentRankDisplayItem
    {
        /// <summary>该局在对局列表里的唯一标识，供点击色块时定位到对应行。</summary>
        public string MatchId { get; set; } = string.Empty;
        public int Rank { get; set; }
        /// <summary>提示文案（"第 13 名 · 点击定位对局"）。</summary>
        public string Display { get; set; } = string.Empty;
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
        /// <summary>本局获得的成就（荣誉徽章）。来自小黑盒对局详情的 tags 字段——列表接口不下发称号。
        /// 用 ObservableCollection 以便行先渲染、成就图标分批填充后自动通知 UI。</summary>
        public ObservableCollection<HonorTitleDisplayItem> HonorTitles { get; } = new();
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
        /// <summary>本局地图名（小黑盒 map_name 原值，如"龙隐洞天"）。</summary>
        public string MapName { get; set; } = string.Empty;
   }

    public class HonorTitleDisplayItem
    {
        public string Icon { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
    }

}
