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
        private readonly HeyboxPlayerRefresher _refresher;
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
            HeyboxPlayerRefresher refresher,
            IUIDispatcher uiDispatcher,
            ILocalizedTextProvider localizedText,
            HeyboxRequestCache requestCache,
            PlayerSearchSuggester searchSuggester,
            HeyboxRoleBinder roleBinder)
        {
            _playerPrefsService = playerPrefsService;
            _tipMessage = tipMessageService;
            _localizationService = localizationService;
            _clipboard = clipboard;
            _playerStatsLoader = playerStatsLoader;
            _battleListLoader = battleListLoader;
            _refresher = refresher;
            _uiDispatcher = uiDispatcher;
            _localizedText = localizedText;
            _requestCache = requestCache;
            _onLanguageChangedHandler = OnLanguageChanged;
            _filterRefreshDebouncer = new TrailingDebouncer(FilterRefreshDebounceMs, RunFilterRefreshAsync);
            Suggestions = new PlayerSearchSuggestionsViewModel(
                uiDispatcher,
                searchSuggester.FetchAsync,
                // 空态「试试绑定角色」的显隐依据：账号是否绑定过角色（三态，拿不准时不显示）。
                roleBinder.HasBoundRoleAsync);
            _suggestionDebouncer = new TrailingDebouncer(SuggestionDebounceMs, RunSuggestionSearchAsync);
            _localizationService.PropertyChanged += _onLanguageChangedHandler;
            Seasons = new ObservableCollection<UnifiedSeason>();
            DetailStats = new ObservableCollection<StatEntryItem>();
            RecentBattles = new RangeObservableCollection<RecentBattleDisplayItem>();
            RefreshStaticLabels();

            // 绑定角色成功 → 自动重查刚绑定的昵称。订阅放 ctor：本页是单例 VM，只订阅这一次。
            eventAggregator.GetEvent<RoleBindSucceededEvent>()
                .Subscribe(OnRoleBindSucceeded, ThreadOption.UIThread);
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
                SeasonKey: null,
                // 搜索进人时不带排数：保持用户当前选的排数不变。
                TeamSize: null);

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

        // === 绑定角色 ===

        /// <summary>
        /// 打开绑定角色弹窗（空态「试试绑定角色」入口）。弹窗走 Overlay Region 弹在整窗之上，
        /// 与公告同一套机制（模糊背景 + 卡片），不占用主内容区导航。
        /// </summary>
        private DelegateCommand? _openBindRoleCommand;
        public DelegateCommand OpenBindRoleCommand =>
            _openBindRoleCommand ??= new DelegateCommand(() =>
            {
                // 下拉是独立顶层 Popup，会浮在弹窗（Overlay）之上——先收起再弹。
                Suggestions.Close();

                // 按需加载 BindRoleModule（与 OpenBattleDetailCommand 同模式）。
                try
                {
                    var moduleManager = containerProvider.Resolve<IModuleManager>();
                    moduleManager.LoadModule(nameof(PageNames.BindRolePage).Replace("Page", "Module"));
                }
                catch { }

                regionManager.RequestNavigate(
                    GlobalConstant.BindRoleRegion,
                    PageNames.BindRolePage,
                    new NavigationParameters
                    {
                        // 把搜索框当前内容带过去预填：用户搜不到的那个昵称多半就是要绑的角色。
                        { NavigationParameterKeys.BindRoleGameId, SearchText },
                    });
            });

        /// <summary>
        /// 绑定角色成功（弹窗发的事件）：对齐网页端——绑定成功后服务端已按绑定关系给出刚绑定的角色，
        /// 事件里就带着这份快照，直接走快照渲染（同队伍卡片跳转路径），不按昵称重搜。
        /// 快照缺失（绑定页查询失败）时才退回按昵称搜索。
        /// </summary>
        private void OnRoleBindSucceeded(RoleBindSucceededEventArgs args)
        {
            _ = ApplyBoundRoleAsync(args);
        }

        private async System.Threading.Tasks.Task ApplyBoundRoleAsync(RoleBindSucceededEventArgs args)
        {
            // 绑定成功 → 账号此后有绑定角色：空态里的绑定引导不再出现。
            Suggestions.MarkAccountBound();

            // 搜索缓存里可能留着绑定前的旧结果（含"这个名字搜不到"的空结果），先整体失效再查。
            _requestCache.Invalidate();

            if (args.BoundRole is { } bound
                && !string.IsNullOrEmpty(bound.RoleId)
                && !string.IsNullOrEmpty(bound.Server))
            {
                // 回填服务端认到的真名（与输入昵称可能有大小写 / 简繁差异），搜索框回填只影响展示。
                _playerPrefsService.Current.PlayerName = string.IsNullOrWhiteSpace(bound.Name) ? args.GameId : bound.Name;
                SetSearchTextSilently(_playerPrefsService.Current.PlayerName);
                _prefetchedPlayer = new PrefetchedPlayer(
                    bound.RoleId,
                    bound.Server,
                    bound.Avatar,
                    bound.Level > 0 ? $"LV.{(int)bound.Level}" : null,
                    // 快照没有赛季列表：退回原路径去取（战绩页会自己拉）。
                    Seasons: null,
                    SeasonKey: null,
                    // 不带排数：保持用户当前选的排数不变（同搜索进人的口径）。
                    TeamSize: null);
                await RefreshAllAsync();
                return;
            }

            // 兜底：快照拿不到时按绑定用的昵称重搜。
            if (string.IsNullOrWhiteSpace(args.GameId)) return;
            _playerPrefsService.Current.PlayerName = args.GameId;
            SetSearchTextSilently(args.GameId);
            await RefreshAllAsync();
        }

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
        /// <summary>
        /// 数据详情：行数与内容完全由后端 <c>overview[]</c> 决定（有几项渲染几格，列数固定 3 列）。
        /// 为空即后端没给数据，网格自然空白，不做补行也不隐藏整块。
        /// </summary>
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
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetSeasonKey),
                    // 卡片当前查的排数：带上后单选切到同一值，"卡片看双排 → 详情也是双排"。
                    navigationContext.Parameters.GetValue<TeamSize?>(NavigationParameterKeys.TargetTeamSize),
                    // 卡片已显示的段位展示字段：段位卡直接用，不必等 home/data 回来（也可能不会回来）。
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetRankIcon),
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetRankName),
                    navigationContext.Parameters.GetValue<double>(NavigationParameterKeys.TargetRankScore),
                    navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.TargetPageRankName),
                    navigationContext.Parameters.GetValue<int>(NavigationParameterKeys.TargetPageStarCount),
                    navigationContext.Parameters.GetValue<bool>(NavigationParameterKeys.TargetPageHasStars));

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
            string? SeasonKey,
            TeamSize? TeamSize,
            /// <summary>卡片上已经显示的段位展示字段：图标 / 段位名 / 段位分 / 上行名 / 星数 / 是否有星。</summary>
            string? RankIcon = null,
            string? RankName = null,
            double RankScore = 0,
            string? PageRankName = null,
            int PageStarCount = 0,
            bool PageHasStars = false);

        /// <summary>
        /// 把卡片带过来的段位展示字段填进段位卡。
        /// <para>
        /// 段位卡的图标与段位名只有 <c>home/data</c> 的 grade 一个来源（见 <see cref="LoadStatsAsync"/>），
        /// 而"卡片点详情"这条路并不保证会再拉一次该接口——同一玩家同赛季同排数时命中提供者缓存会直接返回，
        /// 于是段位图标位置留空。卡片上显示的段位与战绩页本就是同一份数据，直接透传即可。
        /// </para>
        /// <para>
        /// 卡片也是"有什么显示什么"：段位图标没拿到时这里是空串，此时不动 <see cref="RankIcon"/>
        /// 之外的占位状态——保持未定级占位比显示半张段位卡更合理。
        /// </para>
        /// </summary>
        private void ApplyPrefetchedRank(PrefetchedPlayer prefetched)
        {
            if (!string.IsNullOrEmpty(prefetched.RankIcon))
                RankIcon = prefetched.RankIcon;
            if (!string.IsNullOrEmpty(prefetched.RankName))
                RankName = prefetched.RankName;
            if (prefetched.RankScore > 0)
            {
                RankScore = prefetched.RankScore;
                ShowRankScore = true;
            }
            if (!string.IsNullOrEmpty(prefetched.PageRankName))
                PageRankName = prefetched.PageRankName;
            if (prefetched.PageStarCount > 0)
                PageStarCount = prefetched.PageStarCount;
            if (prefetched.PageHasStars)
                PageHasStars = true;
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
            // 但**卡片带快照的跳转（_prefetchedPlayer != null）不能走这条捷径**：
            // 队伍页的筛选（赛季/排数）随时可能已经改了，直接复用会把下面的筛选同步整段跳过——
            // 实测 bug：第一次跳转正常，改完筛选再跳同一个玩家，战绩页"什么都没变化"。
            // 代价可控：同玩家同筛选时走快照分支全部命中提供者缓存（0 次 HTTP），
            // 筛选变了才会真正重新拉数据，而这正是想要的语义。
            if (_prefetchedPlayer is null && IsAlreadyShowingTarget(targetPlayer)) return;

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
            // 不在这里清空头像 / 段位图标地址：本页是单例 VM，离开页面的瞬间把地址抹掉，
            // 而再次进入时 LoadForTargetAndRefreshAsync 对"同一个玩家"走零请求捷径直接 return，
            // 没有任何代码会把地址填回来——表现就是切页回来头像和段位图标整块空白，
            // 必须重新点一次搜索才恢复。换人时的清理由 ResetResultBlocksFor / ClearAllData 负责。
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

        private async void RefreshStats(bool backgroundRefetch = false)
        {
            CancelAndDispose(ref _loadStatsCts);
            _loadStatsCts = new CancellationTokenSource();
            await LoadStatsAsync(_loadStatsCts.Token, backgroundRefetch);
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
            // 启动早期登录态还没恢复完，请求会被服务端按未登录拒绝——先等它走完再发首轮请求
            // （最多等 10 秒，超时照发；恢复失败的情况由正常错误路径处理）。
            // 「启动后很快点进战绩页」的首轮加载失败就是踩在这里：请求发出时 cookie 还没挂上。
            for (var i = 0; i < 40 && !AppStartupState.IsLoginRestored; i++)
                await System.Threading.Tasks.Task.Delay(250, ct).ConfigureAwait(false);

            if (!_playerPrefsService.Current.IsLoaded)
            {
                // 本地游戏账号信息没读到：清掉无主数据 + 收敛加载态，提示放在搜索框下拉里。
                ClearAllData();
                StopAllLoading();
                Suggestions.ShowNotFound();
                return false;
            }

            var localName = _playerPrefsService.Current.PlayerName;
            if (string.IsNullOrEmpty(localName))
            {
                ClearAllData();
                StopAllLoading();
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
                        ApplyPrefetchedRank(prefetched);

                        // 排数单选切到卡片当时的排数（双排卡片 → 双排详情）。
                        // **必须排在 SelectedSeason 赋值之前**：下面改赛季会触发 RefreshStats → LoadStatsAsync，
                        // 而它读的正是 _selectedTeamSize；顺序反了就会先按旧排数查一次、再把新排数查一次，
                        // 白白多打一个 home/data。这里只写字段 + 发通知，不走 setter 的防抖，避免再复制一份请求。
                        if (prefetched.TeamSize is { } prefetchedTeamSize && prefetchedTeamSize != _selectedTeamSize)
                        {
                            _selectedTeamSize = prefetchedTeamSize;
                            RaisePropertyChanged(nameof(SelectedTeamSize));
                        }

                        if (prefetchedSeasons is not { Count: > 0 }) return;

                        // 先把 SelectedSeason 清空再换 Seasons 内容，顺序不能反。
                        // Seasons 是 ComboBox 的 ItemsSource，Clear 会让 ComboBox 把 SelectedItem 置空并
                        // 经 TwoWay 回写 SelectedSeason=null；若此时 SelectedSeason 还指着即将被替换掉的旧实例，
                        // 下面 FirstOrDefault 拿到的 target 与它引用不相等（UnifiedSeason 没有重写 Equals，
                        // 走的是引用相等），选择状态就落在"新列表里找不到的旧对象"上 —— ComboBox 显示空白。
                        // 显式清空后由下一段统一赋 target，选择状态与列表内容必然自洽。
                        _selectedSeason = null;
                        Seasons.Clear();
                        foreach (var season in prefetchedSeasons) Seasons.Add(season);

                        // target 一律取自 Seasons 自身的元素（不是 prefetchedSeasons 里的同值对象）：
                        // ComboBox.SelectedItem 按 Equals 匹配，必须是列表里那个实例才认。
                        var target = Seasons.FirstOrDefault(s => s.SeasonKey == prefetched.SeasonKey) ?? Seasons[0];
                        RaisePropertyChanged(nameof(SelectedSeason));
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
                    // 只提示、不动页面：空态（未查询到用户 + 试试绑定角色）显示在搜索框下拉里，
                    // 当前正在看的数据保持原样——搜错名字不该把已有数据清掉或者是盖住。
                    StopAllLoading();
                    Suggestions.ShowNotFound();
                    return false;
                }
                _roleId = search.RoleIdSimple;
                _sourceContext = new PlayerSourceContext(_roleId, search.Server);
                ResetResultBlocksFor(_roleId);
                var ctx = _sourceContext;

                await ApplyAllBlocksAsync(ctx, localName, ct);
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
                StopAllLoading();
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
                StopAllLoading();
                return false;
            }
        }

        /// <summary>
        /// 收敛三块加载态。LoadAllAsync 的早退 / 失败路径必须调用——否则页面会一直停在转圈
        /// （正常路径由各自的 Apply* 方法在自己的 finally 里关闭）。
        /// </summary>
        private void StopAllLoading()
        {
            IsPlayerInfoLoading = false;
            IsRecentBattlesLoading = false;
            IsStatsLoading = false;
        }

        private System.Threading.Tasks.Task ApplyAllBlocksAsync(
            PlayerSourceContext ctx, string localName, CancellationToken ct)
        {
            // 三块数据（玩家信息 / 赛季 / 对局列表）彼此无依赖：并行发起，且各自完成即绑定自己的 UI，
            // 不再 WhenAll 干等最慢的一路。原实现等三者全回来才统一绑定，最慢的 battles（实测约 5s）
            // 把 userInfo（约 1s）也拖成 5s 才显示——用户整页转圈无法操作。拆开后玩家信息一秒即出，
            // 对局列表区自己转圈，谁快谁先亮。三个 Apply* 各自 try/catch + 关自己的 loading，互不影响。
            var userInfoApply = ApplyUserInfoAsync(ctx, localName, ct);
            var seasonsApply = ApplySeasonsAsync(ctx, ct);
            var battlesApply = ApplyBattlesAsync(ctx, ct);

            // search 已成功即代表"找到玩家"，"搜索成功"提示不必等三块数据全部绑定完。
            // 等三条续接结束仅为让 RefreshAllAsync 的成功判定在数据落地后返回。
            return System.Threading.Tasks.Task.WhenAll(userInfoApply, seasonsApply, battlesApply);
        }

        private const int RefreshSettleDelayMs = 1500;

        private System.Threading.Tasks.Task TrackPageRefresh(PlayerSourceContext ctx, CancellationToken ct)
        {
            return System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // 起跑前先等一小段：用户连续换目标时（快速点候选、连切赛季模式），
                    // 只有停留在他最后选中的那个上才会真正刷新，路过的目标在这里被丢弃。
                    await System.Threading.Tasks.Task.Delay(RefreshSettleDelayMs, ct).ConfigureAwait(false);
                    if (!IsCurrentTarget(ctx)) return;

                    if (!await _playerStatsLoader.IsWaitingUpdateAsync(ctx, ct).ConfigureAwait(false)) return;
                    if (!await _refresher.RefreshAsync(ctx.RoleId, ctx.Server, ct).ConfigureAwait(false)) return;

                    // 刷新期间用户又换了目标：这次结果已经不属于当前页面，直接丢弃。
                    if (!IsCurrentTarget(ctx)) return;

                    _playerStatsLoader.InvalidatePlayer(ctx);
                    _battleListLoader.InvalidatePlayer(ctx);

                    // 赛季列表不重拉：重建 Seasons 会把用户当前选中的筛选重置掉。
                    // 统计走 RefreshStats（它负责取消上一轮），避免与用户切筛选的加载并发交错。
                    var localName = _playerPrefsService.Current.PlayerName;
                    await ApplyUserInfoAsync(ctx, localName, ct).ConfigureAwait(false);
                    await ApplyBattlesAsync(ctx, ct).ConfigureAwait(false);
                    await _uiDispatcher.InvokeAsync(() => RefreshStats(backgroundRefetch: true)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "StatsPage", $"{nameof(TrackPageRefresh)} failed");
                }
            }, ct);
        }

        private bool IsCurrentTarget(PlayerSourceContext ctx)
        {
            var current = _sourceContext;
            return current is not null
                && string.Equals(current.RoleId, ctx.RoleId, StringComparison.Ordinal);
        }

        /// <summary>玩家信息（昵称/等级/UID/头像）：拉取后立即绑定，与赛季/对局互不阻塞。</summary>
        private async System.Threading.Tasks.Task ApplyUserInfoAsync(
            PlayerSourceContext ctx, string localName, CancellationToken ct)
        {
            try
            {
                var userInfo = await _playerStatsLoader.FetchUserInfoAsync(ctx, ct);
                ct.ThrowIfCancellationRequested();

                // 本方法会被后台刷新任务从线程池调用，绑定属性必须回 UI 线程写（同另两块）。
                await _uiDispatcher.InvokeAsync(() =>
                {
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
                }).ConfigureAwait(false);
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

                // 赛季集合绑着下拉，必须回 UI 线程改：本方法可能被队伍快照分支从线程池直接调用，
                // 那时集合变更会撞上「CollectionView 不支持跨线程更改 SourceCollection」，整块数据静默丢失。
                await _uiDispatcher.InvokeAsync(() =>
                {
                    Seasons.Clear();
                    foreach (var s in seasons) Seasons.Add(s);

                    if (Seasons.Count == 0)
                    {
                        // 无赛季则不会触发 LoadStatsAsync，需在此关闭统计区 loading，避免永久转圈。
                        IsStatsLoading = false;
                        return;
                    }

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
                }).ConfigureAwait(false);
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
                if (battlesResult == null) return;

                // 展示后端 matches 单页返回的全部对局（与网页一致，单页约 50 条），不再截断到 10 条。
                // 行对象先在子线程构造（纯 new，不碰任何绑定集合），再一次性 ReplaceAll（仅一次 Reset
                // 通知）——逐条 Add 会触发约 50 次列表布局刷新，是战绩页放开全量后 UI 卡顿的主因。
                var displayItems = battlesResult.Select(BuildBattleDisplayItem).ToList();

                // 必须回 UI 线程替换：本方法会被队伍快照分支从线程池调用，跨线程改 RecentBattles 会抛
                // 「CollectionView 不支持从调度程序线程以外的线程对其 SourceCollection 进行的更改」，
                // 异常被下面 catch 吞掉后表现为「卡片跳过来历史对局永远是空的」。
                await _uiDispatcher.InvokeAsync(() =>
                {
                    // 缓存全量后走筛选视图（默认无筛选=显示全部），供下拉级联本地过滤复用同一份数据。
                    _allBattles.Clear();
                    _allBattles.AddRange(displayItems);
                    ApplyBattleFilter();
                    RecentBattlesProgress = 100;
                }).ConfigureAwait(false);

                // 行已先渲染：成就图标后台分批填充，每批让出 UI 线程，避免一次塞入大量图片卡顿。
                _ = ApplyHonorTitlesAsync(battlesResult, displayItems, ct);
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


        private async System.Threading.Tasks.Task LoadStatsAsync(CancellationToken ct, bool backgroundRefetch = false)
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
                    if (!backgroundRefetch) ClearRecentRanks();
                    return;
                }

                // 段位卡：仅当后端给出有效段位（Grade 非空且有段位名/分数）时才显示实际段位；
                // 否则视为该模式未定级——重置为占位，绝不残留上一次模式的段位（切到未定级双排却
                // 还显示三排"蚀月Ⅳ 3600"就是这个 bug）。判定与右侧数据占位分支保持同一口径。
                var hasRank = stats.Grade != null
                    && (!string.IsNullOrEmpty(stats.Grade.GradeName) || stats.Grade.GradeScore > 0);

                // 后台刷新可能撞上"服务端还在算"的空壳响应：此时保持已有内容不动——
                // 用户什么都没操作，页面不该自己降级成"未定级 + 空数据详情"。
                if (backgroundRefetch && !hasRank && (stats.Stats is null || stats.Stats.Count == 0))
                    return;

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
                // 后端给几项就渲染几格：行数完全由 overview[] 决定，前端不补行、不占位。
                // 列数固定 3 列（XAML 的 UniformGrid Columns="3" Rows="0"），最后一行不足 3 项时留空。
                // 无数据（overview 为空）时集合为空，ItemsControl 自然什么都不渲染——不隐藏整块，
                // 段位卡会跟着切换成"未定级"占位，不会留下突兀的空白。
                foreach (var s in stats.Stats ?? new List<UnifiedStatEntry>())
                {
                    // 行数、顺序、标题全部由后端 overview[] 的 desc 决定（后端给什么就展示什么），
                    // 前端不维护任何 key→标题 的映射表，也不做本地化替换：
                    // 后端下发的 desc 本身就是成品中文标题（"总场次"/"场均伤害"/"夺冠率"…）。
                    var label = string.IsNullOrEmpty(s.Name) ? s.Key : s.Name;
                    // 原样透传服务端值：不补 0、不换算单位（含秒数）、不做任何兜底。
                    var value = s.Value ?? string.Empty;

                    DetailStats.Add(new StatEntryItem
                    {
                        Label = label,
                        Value = value,
                        // overview[] 的 S/A 角标：空串=服务端没给评级，UI 不渲染。
                        Grade = s.Grade,
                        HasGrade = !string.IsNullOrEmpty(s.Grade),
                    });
                }

                // 服务端在这份数据里标了"还需要更新"：后台补一次刷新，界面照常显示已有的。
                // 所有入口（手输搜索 / 选候选 / 从卡片跳转 / 切赛季切模式）最终都汇到这里，
                // 所以只在这一处判断，不需要每个入口各接一遍。
                // backgroundRefetch 为真时不再判断——那是刷新完成后的重拉，否则会自我循环。
                if (!backgroundRefetch && stats.WaitUpdate)
                {
                    var refreshCtx = _sourceContext;
                    if (refreshCtx is not null) _ = TrackPageRefresh(refreshCtx, ct);
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
        /// <summary>服务端 overview[].value 原值，原样展示，前端不换算不兜底。</summary>
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
