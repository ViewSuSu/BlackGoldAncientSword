using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
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
            Seasons = new ObservableCollection<SeasonInfo>();
            DetailStats = new ObservableCollection<StatEntryItem>();
            RecentBattles = new ObservableCollection<RecentBattleDisplayItem>();
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

        private DelegateCommand? _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return;
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
                _playerPrefsService.Current.PlayerName = _playerPrefsService.Current.OriginalPlayerName;
                SearchText = _playerPrefsService.Current.OriginalPlayerName;
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

        private string _rankDisplayWithStars = string.Empty;
        public string RankDisplayWithStars
        {
            get => _rankDisplayWithStars;
            set
            {
                if (_rankDisplayWithStars == value) return;
                _rankDisplayWithStars = value;
                RaisePropertyChanged(nameof(RankDisplayWithStars));
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


        private DelegateCommand<TeamSizeOption>? _selectTeamSizeCommand;
        public DelegateCommand<TeamSizeOption> SelectTeamSizeCommand =>
            _selectTeamSizeCommand ??= new DelegateCommand<TeamSizeOption>(param => { if (param != null) SelectedTeamSize = param.Value; });

        private DelegateCommand<GameModeCategoryOption>? _selectCategoryCommand;
        public DelegateCommand<GameModeCategoryOption> SelectCategoryCommand =>
            _selectCategoryCommand ??= new DelegateCommand<GameModeCategoryOption>(param => { if (param != null) SelectedCategory = param.Value; });

        public static System.ComponentModel.BindingList<TeamSizeOption> TeamSizes { get; } = new(new[] { new TeamSizeOption(TeamSize.Trio), new TeamSizeOption(TeamSize.Duo), new TeamSizeOption(TeamSize.Solo) });
        public static System.ComponentModel.BindingList<GameModeCategoryOption> Categories { get; } = new(new[] { new GameModeCategoryOption(GameModeCategory.Rank), new GameModeCategoryOption(GameModeCategory.Match), new GameModeCategoryOption(GameModeCategory.Tianren) });

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

        private SeasonInfo? _selectedSeason;
        public SeasonInfo? SelectedSeason
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
        public ObservableCollection<SeasonInfo> Seasons { get; }
        public ObservableCollection<StatEntryItem> DetailStats { get; }
        public ObservableCollection<RecentBattleDisplayItem> RecentBattles { get; }

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

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
            {
                TeamSizes.ResetBindings();
                Categories.ResetBindings();
            }
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            RaisePropertyChanged(nameof(IsLocalUser));
            SearchText = _playerPrefsService.Current.PlayerName;
            // 基类签名为 void，无法 await。fire-and-forget；RefreshAllAsync 内部已有 try/catch 兜底。
            _ = RefreshAllAsync();
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
            RankDisplayWithStars = string.Empty;
            RankTierScore = 0;
            TotalGames = "0";
            TopOneCount = "0";
            TopFiveCount = "0";
            AvgDamage = "0";
            DetailStats.Clear();
            RecentBattles.Clear();
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

            if (!ct.IsCancellationRequested)
            {
                if (success)
                    _tipMessage.ShowInfo(L("Stats.SearchSuccess", "搜索成功"));
                else if (!string.IsNullOrEmpty(_playerPrefsService.Current.PlayerName))
                    _tipMessage.ShowError(L("Stats.SearchError", "搜索失败，请检查网络后重试"));
            }
        }

        private string L(string key, string fallback) => _localizedText.Get(key, fallback);

        private async System.Threading.Tasks.Task<bool> LoadAllAsync(CancellationToken ct)
        {
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
                var search = await _playerStatsLoader.SearchRoleByNameAsync(localName, ct);
                if (search?.Data == null) { ShowNotFound = true; ClearAllData(); _tipMessage.ShowError(search?.Msg ?? L("Stats.LoadError", "加载战绩失败，请检查网络后重试。")); return false; }
                _roleId = search.Data.RoleIdSimple ?? string.Empty;
                if (string.IsNullOrEmpty(_roleId)) { ShowNotFound = true; ClearAllData(); _tipMessage.ShowError(L("Stats.PlayerNotFound", "未找到该玩家，请检查名称是否正确")); return false; }

                // Fire all three requests in parallel
                var userInfoTask = _playerStatsLoader.FetchUserInfoAsync(_roleId, ct);
                var seasonsTask = _playerStatsLoader.FetchSeasonsAsync(ct);
                var battlesTask = _battleListLoader.FetchBattleListAsync(_roleId, ct);

                await System.Threading.Tasks.Task.WhenAll(userInfoTask, seasonsTask, battlesTask);
                ct.ThrowIfCancellationRequested();

                // 用 await 而非 .Result：
                // 1) .Result 会把 TaskCanceledException 重新包装成 AggregateException，
                //    导致下游 `catch (OperationCanceledException)` 漏接；
                // 2) await 与上面的 userInfoTask/seasonsTask 取值方式一致，异常类型对称。
                // 此时 WhenAll 已完成，await 不会再产生续接调度开销。
                var battlesResult = await battlesTask;
                // Process userInfo and seasons first (fast responses)
                var userInfo = await userInfoTask;
                if (userInfo?.Code == 200 && userInfo.Data != null)
                {
                    var d = userInfo.Data;
                    UserName = d.Role?.RoleName ?? d.NickName ?? localName;
                    Level = $"Lv.{d.Role?.RoleLevel ?? 0}";
                    UID = d.Role?.Uid ?? string.Empty;
                    AvatarUrl = d.Role?.HeadIcon ?? string.Empty;
                }
                PlayerInfoProgress = 100;
                IsPlayerInfoLoading = false;

                var seasonsResult = await seasonsTask;
                if (seasonsResult?.Code == 200 && seasonsResult.Data != null)
                {
                    Seasons.Clear();
                    foreach (var s in seasonsResult.Data)
                        if (s.Code > 0) Seasons.Add(s);
                    if (Seasons.Count > 0) SelectedSeason = Seasons[0];
                }

                // Populate recent battles basic info, then serially fetch team performance
                if (battlesResult?.Code == 200 && battlesResult.Data?.List != null)
                {
                    var battleItems = battlesResult.Data.List.Take(10).ToList();

                    RecentBattles.Clear();
                    for (int i = 0; i < battleItems.Count; i++)
                    {
                        var b = battleItems[i];
                        var modeCode = (int)(b.Subtype ?? b.GameMode ?? 0);
                        RecentBattles.Add(new RecentBattleDisplayItem
                        {
                            Rank = b.Rank ?? 0,
                            HonorTitles = new ObservableCollection<HonorTitleDisplayItem>(),
                            HeroIcon = b.Hero?.HeroIcon ?? string.Empty,
                            HeroName = b.Hero?.HeroName ?? "Unknown",
                            GameModeText = FormatGameMode(modeCode),
                            GameModeCategoryText = FormatGameModeCategory(modeCode),
                            GameModeTeamSizeText = FormatGameModeTeamSize(modeCode),
                            GameMode = modeCode,
                            Kill = (int)(b.Kill ?? 0),
                            Damage = (int)(b.Damage ?? 0),
                            ScoreNumber = GetRankTierScore((b.RoundRankScore ?? 0), modeCode),
                            ScoreDiff = (b.RoundRankScore ?? 0) - (b.BeginRankScore ?? 0),
                            RankDisplayText = GetRankNameForScore((b.RoundRankScore ?? 0), modeCode),
                            StarCount = GetStarCount((b.RoundRankScore ?? 0), modeCode),
                            HasStars = IsTianxuanMode(modeCode) && (b.RoundRankScore ?? 0) >= 4500,
                            ScoreDiffDisplay = FormatScoreDiff((b.RoundRankScore ?? 0) - (b.BeginRankScore ?? 0)),
                            BattleTime = FormatUnixTime(b.BattleEndTime ?? 0)
                        });
                    }

                    // Loading spinner done — launch team performance fetch in background
                    RecentBattlesProgress = 100;
                    IsRecentBattlesLoading = false;
                    _ = FetchHonorTitlesSeriallyAsync(battleItems, ct);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                // 导航离开或过滤条件已变更——不是错误
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StatsPage] LoadRecentBattlesAsync failed: {ex}");
                _tipMessage.ShowError(ex.Message);
                return false;
            }
            finally
            {
                IsPlayerInfoLoading = false;
                IsRecentBattlesLoading = false;
                IsStatsLoading = false;
            }
        }

        private async System.Threading.Tasks.Task FetchHonorTitlesSeriallyAsync(
            System.Collections.Generic.List<RecentBattleItem> battleItems, CancellationToken ct)
        {
            try
            {
                await _battleListLoader.FetchHonorTitlesForListAsync(
                    _roleId,
                    battleItems,
                    (index, titles) => { _ = _uiDispatcher.InvokeAsync(() => ApplyHonorTitlesToBattle(index, titles)); },
                    ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(StatsPageViewModel)}] FetchHonorTitlesSeriallyAsync failed: {ex}");
            }
        }

        /// <summary>
        /// 把单条对局拉回的 HonorTitles 映射到对应 UI Item。
        /// 映射逻辑保留在 VM：这是从 DTO 到 UI Item 的转换，属于显示层职责。
        /// </summary>
        private void ApplyHonorTitlesToBattle(int index, HonorTitleInfo[] titles)
        {
            if (index < 0 || index >= RecentBattles.Count) return;

            var existing = RecentBattles[index].HonorTitles;
            existing.Clear();
            if (titles.Length > 0)
            {
                foreach (var t in titles.Adapt<List<HonorTitleDisplayItem>>())
                    existing.Add(t);
            }
            RecentBattlesProgress = Math.Min(100, RecentBattlesProgress + 10);
        }

        private async System.Threading.Tasks.Task LoadStatsAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_roleId) || SelectedSeason == null)
                return;

            IsStatsLoading = true;
            StatsProgress = 0;
            try
            {
                var gameMode = GameModeExtensions.FromCategoryAndTeamSize(_selectedCategory, _selectedTeamSize);

                var stats = await _playerStatsLoader.FetchPlayerStatsAsync(
                    _roleId, SelectedSeason.Code, gameMode, ct);

                if (stats?.Data == null) { _tipMessage.ShowError(stats?.Msg ?? L("Stats.LoadError", "加载战绩失败，请检查网络后重试。")); return; }

                var data = stats.Data;

                if (data.Grade != null)
                {
                    RankName = data.Grade.GradeName ?? string.Empty;
                    RankIcon = data.Grade.GradeIcon ?? string.Empty;
                    RankScore = data.Grade.GradeScore ?? 0;
                    RankLevel = data.Grade.GradeLevel ?? string.Empty;
                    PageRankName = GetRankNameForScore(data.Grade.GradeScore ?? 0, (int)gameMode);
                    PageStarCount = GetStarCount(data.Grade.GradeScore ?? 0, (int)gameMode);
                    PageHasStars = IsTianxuanMode((int)gameMode) && (data.Grade.GradeScore ?? 0) >= 4500;
                    RankDisplayWithStars = FormatPageRankDisplay(data.Grade.GradeScore ?? 0, (int)gameMode);
                    RankTierScore = GetRankTierScore(data.Grade.GradeScore ?? 0, (int)gameMode);
                }

                DetailStats.Clear();
                if (data.Stats != null)
                {
                    foreach (var s in data.Stats)
                    {
                        var label = FormatStatLabel(s.Key, s.Name);
                        var value = s.Value ?? "0";

                        // Convert survival time from seconds to mm:ss format
                        if ((s.Key ?? "").Contains("live_time", StringComparison.OrdinalIgnoreCase) || (s.Name ?? "").Contains("生存"))
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
                    TotalGames = FindStatValue(data.Stats, "对局", "场次", "game", "battle", "round");
                    TopOneCount = FindStatValue(data.Stats, "第一", "冠军", "吃鸡", "champion", "top1", "win");
                    TopFiveCount = FindStatValue(data.Stats, "前五", "top5");
                    AvgDamage = FindStatValue(data.Stats, "场均", "场均伤害", "伤害", "damage", "avgDamage");
                }
                else
                {
                    TotalGames = "0";
                    TopOneCount = "0";
                    TopFiveCount = "0";
                    AvgDamage = "0";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StatsPage] LoadStatsAsync failed: {ex}");
                _tipMessage.ShowError(ex.Message);
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
            return fallbackName ?? key ?? string.Empty;
        }

        private static string FindStatValue(List<StatEntry> stats, params string[] keyPatterns)
        {
            foreach (var s in stats)
            {
                var label = (s.Name ?? s.Key ?? string.Empty).ToLowerInvariant();
                foreach (var pattern in keyPatterns)
                {
                    if (label.Contains(pattern.ToLowerInvariant()))
                        return s.Value ?? "0";
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

        private string FormatGameMode(int gameMode)
        {
            var enumValue = gameMode switch
            {
                1 => GameMode.RankSolo,
                12 => GameMode.RankDuo,
                2 => GameMode.RankTrio,
                6 => GameMode.MatchSolo,
                9 => GameMode.MatchDuo,
                7 => GameMode.MatchTrio,
                4 => GameMode.TianrenSolo,
                13 => GameMode.TianrenDuo,
                5 => GameMode.TianrenTrio,
                _ => (GameMode?)null
            };

            if (enumValue.HasValue)
            {
                var key = "GameMode." + enumValue.Value.ToString();
                return _localizedText.Get(key, enumValue.Value.ToString());
            }

            var unknownKey = _localizedText.Get("GameMode.Unknown", "Unknown");
            return $"{unknownKey}({gameMode})";
        }

        private string FormatGameModeCategory(int gameMode)
        {
            if (Enum.IsDefined(typeof(GameMode), gameMode))
            {
                try
                {
                    var category = ((GameMode)gameMode).GetCategory();
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
            if (Enum.IsDefined(typeof(GameMode), gameMode))
            {
                try
                {
                    var teamSize = ((GameMode)gameMode).GetTeamSize();
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
                System.Diagnostics.Debug.WriteLine($"[{nameof(StatsPageViewModel)}] FormatUnixTime failed for {unixMilliseconds}: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool IsTianxuanMode(double gameMode)
        {
            var mode = (int)gameMode; return Enum.IsDefined(typeof(GameMode), mode) && ((GameMode)mode).GetCategory() == GameModeCategory.Rank;
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
                return L("Stats.RankName.Solo.0", "青铜");
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
            if (score >= 4500) return (int)((score - 4500) / 100);
            return 0;
        }

        private static string FormatScoreDiff(double diff)
        {
            var sign = diff >= 0 ? "+" : "";
            return "(" + sign + diff + ")";
        }

        private string FormatPageRankDisplay(double score, int gameMode = 0)
        {
            var rankName = GetRankNameForScore(score, gameMode);
            var stars = GetStarCount(score, gameMode);
            if (IsTianxuanMode(gameMode) && score >= 4500)
                return rankName + " " + stars + "?";
            return rankName;
        }

        private static double GetRankTierScore(double score, int gameMode = 0)
        {
            if (!IsTianxuanMode(gameMode)) return score;
            if (score >= 4500) return (score - 4500) % 100;
            return score;
        }

    }

    public class StatEntryItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class RecentBattleDisplayItem
    {
        public double Rank { get; set; }
        public ObservableCollection<HonorTitleDisplayItem> HonorTitles { get; set; } = new();
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
        public double StarCount { get; set; }
        public bool HasStars { get; set; }
        public string ScoreDiffDisplay { get; set; } = string.Empty;
        public string BattleTime { get; set; } = string.Empty;
    }

    public class HonorTitleDisplayItem
    {
        public string Icon { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HonorName { get; set; } = string.Empty;
        public string HonorDesc { get; set; } = string.Empty;
    }

    public class TeamSizeOption
    {
        public TeamSize Value { get; }
        public TeamSizeOption(TeamSize value) => Value = value;
        public string DisplayName =>
            System.Windows.Application.Current?.TryFindResource("GameMode." + Value.ToString()) as string ?? Value.ToString();
    }

    public class GameModeCategoryOption
    {
        public GameModeCategory Value { get; }
        public GameModeCategoryOption(GameModeCategory value) => Value = value;
        public string DisplayName =>
            System.Windows.Application.Current?.TryFindResource("GameMode." + Value.ToString()) as string ?? Value.ToString();
    }
}
