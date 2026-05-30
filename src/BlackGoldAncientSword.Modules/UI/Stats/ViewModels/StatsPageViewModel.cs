using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Events;
using System.Collections.Generic;
using BlackGoldAncientSword.Framework.Services.Abstractions;


namespace BlackGoldAncientSword.Modules.UI.Stats.ViewModels
{
    public class StatsPageViewModel : ViewModelBase
    {
        private readonly IPlayerPrefsService _playerPrefsService;
        private readonly ITipMessageService _tipMessage;
        private CancellationTokenSource? _loadAllCts;
        private CancellationTokenSource? _loadStatsCts;

        public StatsPageViewModel(IPlayerPrefsService playerPrefsService, ILocalizationService localizationService, ITipMessageService tipMessageService)
        {
            _playerPrefsService = playerPrefsService;
            _tipMessage = tipMessageService;
            localizationService.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(localizationService.CurrentLanguage)) { TeamSizes.ResetBindings(); Categories.ResetBindings(); } };
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
                if (SetProperty(ref _userName, value))
                    RaisePropertyChanged(nameof(IsLocalUser));
            }
        }

        private string _uid = string.Empty;
        public string UID
        {
            get => _uid;
            set => SetProperty(ref _uid, value);
        }

        private string _level = string.Empty;
        public string Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        private string _avatarUrl = string.Empty;
        public string AvatarUrl
        {
            get => _avatarUrl;
            set => SetProperty(ref _avatarUrl, value);
        }

        private DelegateCommand? _copyUserNameCommand;
        public DelegateCommand CopyUserNameCommand =>
            _copyUserNameCommand ??= new DelegateCommand(() =>
            {
                Clipboard.SetText(UserName);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(Application.Current?.TryFindResource("Stats.CopySuccess") as string ?? "复制成功"));
            });

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
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
                Clipboard.SetText(UID);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(Application.Current?.TryFindResource("Stats.CopySuccess") as string ?? "复制成功"));
            });

        private DelegateCommand? _refreshAllCommand;
        public DelegateCommand RefreshAllCommand =>
            _refreshAllCommand ??= new DelegateCommand(async () => await RefreshAllAsync());


        // === Rank ===
        private string _rankName = string.Empty;
        public string RankName
        {
            get => _rankName;
            set => SetProperty(ref _rankName, value);
        }

        private string _rankIcon = string.Empty;
        public string RankIcon
        {
            get => _rankIcon;
            set => SetProperty(ref _rankIcon, value);
        }

        private int _rankScore;
        public int RankScore
        {
            get => _rankScore;
            set => SetProperty(ref _rankScore, value);
        }

        private string _rankLevel = string.Empty;
        public string RankLevel
        {
            get => _rankLevel;
            set => SetProperty(ref _rankLevel, value);
        }

        private string _rankDisplayWithStars = string.Empty;
        public string RankDisplayWithStars
        {
            get => _rankDisplayWithStars;
            set => SetProperty(ref _rankDisplayWithStars, value);
        }

        private int _rankTierScore;
        public int RankTierScore
        {
            get => _rankTierScore;
            set => SetProperty(ref _rankTierScore, value);
        }

        private string _pageRankName = string.Empty;
        public string PageRankName
        {
            get => _pageRankName;
            set => SetProperty(ref _pageRankName, value);
        }

        private int _pageStarCount;
        public int PageStarCount
        {
            get => _pageStarCount;
            set => SetProperty(ref _pageStarCount, value);
        }

        private bool _pageHasStars;
        public bool PageHasStars
        {
            get => _pageHasStars;
            set => SetProperty(ref _pageHasStars, value);
        }

        // === Rank Stats ===
        private string _totalGames = "0";
        public string TotalGames
        {
            get => _totalGames;
            set => SetProperty(ref _totalGames, value);
        }

        private string _topOneCount = "0";
        public string TopOneCount
        {
            get => _topOneCount;
            set => SetProperty(ref _topOneCount, value);
        }

        private string _topFiveCount = "0";
        public string TopFiveCount
        {
            get => _topFiveCount;
            set => SetProperty(ref _topFiveCount, value);
        }

        private string _avgDamage = "0";
        public string AvgDamage
        {
            get => _avgDamage;
            set => SetProperty(ref _avgDamage, value);
        }

        // === Filters ===
        private GameModeCategory _selectedCategory = GameModeCategory.Rank;
        public GameModeCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    RefreshStats();
            }
        }

        private TeamSize _selectedTeamSize = TeamSize.Trio;
        public TeamSize SelectedTeamSize
        {
            get => _selectedTeamSize;
            set
            {
                if (SetProperty(ref _selectedTeamSize, value))
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
        };

        private SeasonInfo? _selectedSeason;
        public SeasonInfo? SelectedSeason
        {
            get => _selectedSeason;
            set
            {
                if (SetProperty(ref _selectedSeason, value))
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
            set => SetProperty(ref _isPlayerInfoLoading, value);
        }

        private double _playerInfoProgress;
        public double PlayerInfoProgress
        {
            get => _playerInfoProgress;
            set => SetProperty(ref _playerInfoProgress, value);
        }

        private bool _isRecentBattlesLoading;
        public bool IsRecentBattlesLoading
        {
            get => _isRecentBattlesLoading;
            set => SetProperty(ref _isRecentBattlesLoading, value);
        }

        private double _recentBattlesProgress;
        public double RecentBattlesProgress
        {
            get => _recentBattlesProgress;
            set => SetProperty(ref _recentBattlesProgress, value);
        }

        private bool _isStatsLoading;
        public bool IsStatsLoading
        {
            get => _isStatsLoading;
            set => SetProperty(ref _isStatsLoading, value);
        }

        private double _statsProgress;
        public double StatsProgress
        {
            get => _statsProgress;
            set => SetProperty(ref _statsProgress, value);
        }

        private bool _showNotFound;
        public bool ShowNotFound
        {
            get => _showNotFound;
            set => SetProperty(ref _showNotFound, value);
        }

        private string _roleId = string.Empty;

        protected override async void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            RaisePropertyChanged(nameof(IsLocalUser));
            SearchText = _playerPrefsService.Current.PlayerName;
            await RefreshAllAsync();
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            CancelAndDispose(ref _loadAllCts);
            CancelAndDispose(ref _loadStatsCts);
            ClearImageBindings();
            base.OnNavigatedFromExecute(navigationContext);
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
                else
                    _tipMessage.ShowError(L("Stats.SearchError", "搜索失败，请检查网络后重试"));
            }
        }

        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;

        private async System.Threading.Tasks.Task<bool> LoadAllAsync(CancellationToken ct)
        {
            var localName = _playerPrefsService.Current.PlayerName;
            if (string.IsNullOrEmpty(localName))
            {
                _tipMessage.ShowError(L("Stats.NoPlayerName", "请先在设置中配置玩家名称"));
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
                var search = await NarakaApiClient.SearchRecordAsync(localName, ct);
                if (search?.Data == null) { ShowNotFound = true; ClearAllData(); _tipMessage.ShowError(search?.Msg ?? L("Stats.LoadError", "加载战绩失败，请检查网络后重试。")); return false; }
                _roleId = search.Data.RoleIdSimple ?? string.Empty;
                if (string.IsNullOrEmpty(_roleId)) { ShowNotFound = true; ClearAllData(); _tipMessage.ShowError(L("Stats.PlayerNotFound", "未找到该玩家，请检查名称是否正确")); return false; }

                // Fire all three requests in parallel
                var userInfoTask = NarakaApiClient.GetUserInfoAsync(_roleId, ct);
                var seasonsTask = NarakaApiClient.QuerySeasonsAsync(ct);
                var battlesTask = NarakaApiClient.GetRecentBattlesAsync(_roleId, ct: ct);

                await System.Threading.Tasks.Task.WhenAll(userInfoTask, seasonsTask, battlesTask);
                ct.ThrowIfCancellationRequested();

                var battlesResult = battlesTask.Result;
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
                        RecentBattles.Add(new RecentBattleDisplayItem
                        {
                            Rank = b.Rank ?? 0,
                            HonorTitles = new ObservableCollection<HonorTitleDisplayItem>(),
                            HeroIcon = b.Hero?.HeroIcon ?? string.Empty,
                            HeroName = b.Hero?.HeroName ?? "Unknown",
                            GameModeText = FormatGameMode(b.GameMode ?? 0),
                            GameModeCategoryText = FormatGameModeCategory(b.GameMode ?? 0),
                            GameModeTeamSizeText = FormatGameModeTeamSize(b.GameMode ?? 0),
                            GameMode = b.GameMode ?? 0,
                            Kill = b.Kill ?? 0,
                            Damage = b.Damage ?? 0,
                            ScoreNumber = GetRankTierScore((b.RoundRankScore ?? 0), b.GameMode ?? 0),
                            ScoreDiff = (b.RoundRankScore ?? 0) - (b.BeginRankScore ?? 0),
                            RankDisplayText = GetRankNameForScore((b.RoundRankScore ?? 0), b.GameMode ?? 0),
                            StarCount = GetStarCount((b.RoundRankScore ?? 0), b.GameMode ?? 0),
                            HasStars = IsTianxuanMode(b.GameMode ?? 0) && (b.RoundRankScore ?? 0) >= 4500,
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
                // Navigation away or filter changed 鈥?not an error
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
            for (int i = 0; i < battleItems.Count; i++)
            {
                if (ct.IsCancellationRequested) return;
                await FetchAndUpdateHonorTitlesAsync(battleItems[i].BattleId.ToString(), i, ct);
            }
        }

        private async System.Threading.Tasks.Task FetchAndUpdateHonorTitlesAsync(
            string battleId, int index, CancellationToken ct)
        {
            try
            {
                var detail = await NarakaApiClient.GetBattleDetailAsync(_roleId, battleId, ct);
                if (detail?.Code == 200 && detail.Data?.HonorTitles != null && index < RecentBattles.Count)
                {
                    var existing = RecentBattles[index].HonorTitles;
                    existing.Clear();
                    foreach (var t in detail.Data.HonorTitles.Adapt<List<HonorTitleDisplayItem>>())
                        existing.Add(t);
                }
                RecentBattlesProgress = Math.Min(100, RecentBattlesProgress + 10);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StatsPage] FetchAndUpdateHonorTitlesAsync failed: {ex}");
            }
        }

        private async System.Threading.Tasks.Task LoadStatsAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_roleId) || SelectedSeason == null)
                return;

            IsStatsLoading = true;
            StatsProgress = 0;
            try
            {
                var gameMode = ResolveGameMode(_selectedCategory, _selectedTeamSize);

                var stats = await NarakaApiClient.GetPlayerStatsAsync(
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

        
        private static string FormatStatLabel(string? key, string? fallbackName)
        {
            if (!string.IsNullOrEmpty(key) && StatKeyToResourceKey.TryGetValue(key, out var resourceKey))
            {
                return Application.Current?.TryFindResource(resourceKey) as string ?? fallbackName ?? key;
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

        private static string FormatSurvivalTime(string secondsStr)
        {
            if (double.TryParse(secondsStr, out double seconds))
            {
                var minutes = (int)(seconds / 60);
                var remainSeconds = (int)(seconds % 60);
                var minUnit = Application.Current?.TryFindResource("Stats.Minute") as string ?? "分";
                var secUnit = Application.Current?.TryFindResource("Stats.Second") as string ?? "秒";
                return $"{minutes}{minUnit}{remainSeconds:D2}{secUnit}";
            }
            return secondsStr;
        }

        private static string FormatGameMode(int gameMode)
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
                return Application.Current?.TryFindResource(key) as string ?? enumValue.Value.ToString();
            }

            var unknownKey = Application.Current?.TryFindResource("GameMode.Unknown") as string ?? "Unknown";
            return $"{unknownKey}({gameMode})";
        }

        private static string FormatGameModeCategory(int gameMode)
        {
            var enumValue = gameMode switch
            {
                1 => GameModeCategory.Rank,
                12 => GameModeCategory.Rank,
                2 => GameModeCategory.Rank,
                6 => GameModeCategory.Match,
                9 => GameModeCategory.Match,
                7 => GameModeCategory.Match,
                4 => GameModeCategory.Tianren,
                13 => GameModeCategory.Tianren,
                5 => GameModeCategory.Tianren,
                _ => (GameModeCategory?)null
            };

            if (enumValue.HasValue)
            {
                var key = "GameMode." + enumValue.Value.ToString();
                return Application.Current?.TryFindResource(key) as string ?? enumValue.Value.ToString();
            }

            return Application.Current?.TryFindResource("GameMode.Unknown") as string ?? "Unknown";
        }

        private static string FormatGameModeTeamSize(int gameMode)
        {
            var enumValue = gameMode switch
            {
                1 => TeamSize.Solo,
                12 => TeamSize.Duo,
                2 => TeamSize.Trio,
                6 => TeamSize.Solo,
                9 => TeamSize.Duo,
                7 => TeamSize.Trio,
                4 => TeamSize.Solo,
                13 => TeamSize.Duo,
                5 => TeamSize.Trio,
                _ => (TeamSize?)null
            };

            if (enumValue.HasValue)
            {
                var key = "GameMode." + enumValue.Value.ToString();
                return Application.Current?.TryFindResource(key) as string ?? enumValue.Value.ToString();
            }

            return Application.Current?.TryFindResource("GameMode.Unknown") as string ?? "Unknown";
        }

        private static string FormatUnixTime(long unixMilliseconds)
        {
            try
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).LocalDateTime;
                return dt.ToString("yyyy/MM/dd HH:mm");
            }
            catch { return string.Empty; }
        }

        
        private static GameMode ResolveGameMode(GameModeCategory category, TeamSize size)
        {
            return (category, size) switch
            {
                (GameModeCategory.Rank, TeamSize.Solo) => GameMode.RankSolo,
                (GameModeCategory.Rank, TeamSize.Duo) => GameMode.RankDuo,
                (GameModeCategory.Rank, TeamSize.Trio) => GameMode.RankTrio,
                (GameModeCategory.Match, TeamSize.Solo) => GameMode.MatchSolo,
                (GameModeCategory.Match, TeamSize.Duo) => GameMode.MatchDuo,
                (GameModeCategory.Match, TeamSize.Trio) => GameMode.MatchTrio,
                (GameModeCategory.Tianren, TeamSize.Solo) => GameMode.TianrenSolo,
                (GameModeCategory.Tianren, TeamSize.Duo) => GameMode.TianrenDuo,
                (GameModeCategory.Tianren, TeamSize.Trio) => GameMode.TianrenTrio,
                _ => GameMode.RankTrio
            };
        }

        private static string FormatScore(int begin, int round)
        {
            var diff = round - begin;
            var sign = diff >= 0 ? "+" : "";
            return string.Format("{0} -> {1} ({2}{3})", begin, round, sign, diff);
        }
        private static bool IsTianxuanMode(int gameMode)
        {
            return gameMode == 1 || gameMode == 12 || gameMode == 2;
        }

        private static string GetRankNameForScore(int score, int gameMode = 0)
        {
            if (IsTianxuanMode(gameMode))
            {
                if (score >= 7000) return L("Stats.RankName.Solo.7000", "无量梵天");
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

        private static int GetStarCount(int score, int gameMode = 0)
        {
            if (!IsTianxuanMode(gameMode)) return 0;
            if (score >= 7000) return (score - 7000) / 100;
            if (score >= 6000) return (score - 6000) / 100;
            if (score >= 5000) return (score - 5000) / 100;
            if (score >= 4500) return (score - 4500) / 100;
            return 0;
        }

        private static string FormatScoreDiff(int diff)
        {
            var sign = diff >= 0 ? "+" : "";
            return "(" + sign + diff + ")";
        }

        private static string FormatPageRankDisplay(int score, int gameMode = 0)
        {
            var rankName = GetRankNameForScore(score, gameMode);
            var stars = GetStarCount(score, gameMode);
            if (IsTianxuanMode(gameMode) && score >= 4500)
                return rankName + " " + stars + "?";
            return rankName;
        }

        private static int GetRankTierScore(int score, int gameMode = 0)
        {
            if (!IsTianxuanMode(gameMode)) return score;
            if (score >= 7000) return (score - 7000) % 100;
            if (score >= 6000) return (score - 6000) % 100;
            if (score >= 5000) return (score - 5000) % 100;
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
        public int Rank { get; set; }
        public ObservableCollection<HonorTitleDisplayItem> HonorTitles { get; set; } = new();
        public string HeroIcon { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public string GameModeText { get; set; } = string.Empty;
        public int GameMode { get; set; }
        public string GameModeCategoryText { get; set; } = string.Empty;
        public string GameModeTeamSizeText { get; set; } = string.Empty;
        public int Kill { get; set; }
        public int Damage { get; set; }
        public int ScoreNumber { get; set; }
        public int ScoreDiff { get; set; }
        public string RankDisplayText { get; set; } = string.Empty;
        public int StarCount { get; set; }
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

