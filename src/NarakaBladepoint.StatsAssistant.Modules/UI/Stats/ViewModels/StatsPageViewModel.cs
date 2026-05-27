using System.Collections.ObjectModel;
using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;
using NarakaBladepoint.StatsAssistant.Framework.Http;
using NarakaBladepoint.StatsAssistant.Framework.Http.Generated;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;
using System.Windows;
using NarakaBladepoint.StatsAssistant.Framework.Core.Events;
using System.Collections.Generic;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;


namespace NarakaBladepoint.StatsAssistant.Modules.UI.Stats.ViewModels
{
    public class StatsPageViewModel : ViewModelBase
    {
        private readonly IPlayerPrefsService _playerPrefsService;
        private CancellationTokenSource? _loadAllCts;
        private CancellationTokenSource? _loadStatsCts;

        public StatsPageViewModel(IPlayerPrefsService playerPrefsService, ILocalizationService localizationService)
        {
            _playerPrefsService = playerPrefsService;
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
            set => SetProperty(ref _userName, value);
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

        private DelegateCommand? _copyUIDCommand;
        public DelegateCommand CopyUIDCommand =>
            _copyUIDCommand ??= new DelegateCommand(() =>
            {
                Clipboard.SetText(UID);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(Application.Current?.TryFindResource("Stats.CopySuccess") as string ?? "复制成功"));
            });


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

        private string _roleId = string.Empty;

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            CancelAndDispose(ref _loadAllCts);
            _loadAllCts = new CancellationTokenSource();
            _ = LoadAllAsync(_loadAllCts.Token);
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            CancelAndDispose(ref _loadAllCts);
            CancelAndDispose(ref _loadStatsCts);
            ClearImageBindings();
            base.OnNavigatedFromExecute(navigationContext);
        }

        private void RefreshStats()
        {
            CancelAndDispose(ref _loadStatsCts);
            _loadStatsCts = new CancellationTokenSource();
            _ = LoadStatsAsync(_loadStatsCts.Token);
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

        private async System.Threading.Tasks.Task LoadAllAsync(CancellationToken ct)
        {
            var localName = _playerPrefsService.Current.PlayerName;
            if (string.IsNullOrEmpty(localName))
                return;

            IsPlayerInfoLoading = true;
            PlayerInfoProgress = 0;
            IsRecentBattlesLoading = true;
            RecentBattlesProgress = 0;
            IsStatsLoading = true;
            StatsProgress = 0;

            try
            {
                var search = await NarakaApiClient.SearchRecordAsync(localName, ct);
                if (search?.Code != 200 || search.Data == null) { return; }
                _roleId = search.Data.RoleIdMiniProgram ?? string.Empty;
                if (string.IsNullOrEmpty(_roleId)) { return; }

                // Fire userInfo + seasons in background; get battles list first
                var userInfoTask = NarakaApiClient.GetUserInfoAsync(_roleId, ct);
                var seasonsTask = NarakaApiClient.QuerySeasonsAsync(ct);
                var battlesResult = await NarakaApiClient.GetRecentBattlesAsync(_roleId, ct: ct);
                ct.ThrowIfCancellationRequested();

                // As soon as battles list arrives, fire all 10 detail requests concurrently
                System.Threading.Tasks.Task? detailsTask = null;
                if (battlesResult?.Code == 200 && battlesResult.Data?.List != null)
                {
                    var battleItems = battlesResult.Data.List.Take(10).ToList();

                    // Populate recent battles with basic info first (no honor titles yet)
                    RecentBattles.Clear();
                    for (int i = 0; i < battleItems.Count; i++)
                    {
                        var b = battleItems[i];
                        RecentBattles.Add(new RecentBattleDisplayItem
                        {
                            Rank = b.Rank,
                            HonorTitles = new ObservableCollection<HonorTitleDisplayItem>(),
                            HeroIcon = b.Hero?.HeroIcon ?? string.Empty,
                            HeroName = b.Hero?.HeroName ?? "Unknown",
                            GameModeText = FormatGameMode(b.GameMode),
                            GameMode = b.GameMode,
                            Kill = b.Kill,
                            Damage = b.Damage,
                            ScoreNumber = GetRankTierScore(b.RoundRankScore, b.GameMode),
                            ScoreDiff = b.RoundRankScore - b.BeginRankScore,
                            RankDisplayText = GetRankNameForScore(b.RoundRankScore, b.GameMode),
                            StarCount = GetStarCount(b.RoundRankScore, b.GameMode),
                            HasStars = IsTianxuanMode(b.GameMode) && b.RoundRankScore >= 4500,
                            ScoreDiffDisplay = FormatScoreDiff(b.RoundRankScore - b.BeginRankScore),
                            BattleTime = FormatUnixTime(b.BattleEndTime)
                        });
                    }

                    // Launch all 10 detail requests concurrently 鈥?don't wait yet
                    var detailTasks = battleItems
                        .Select((b, index) => FetchAndUpdateHonorTitlesAsync(b.BattleId.ToString(), index, ct))
                        .ToArray();
                    detailsTask = System.Threading.Tasks.Task.WhenAll(detailTasks);
                }
                // Now process userInfo and seasons while detail requests are in-flight
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
                // Wait for all detail requests to finish
                if (detailsTask != null)
                    await detailsTask;

                RecentBattlesProgress = 100;
                IsRecentBattlesLoading = false;
            }
            catch (OperationCanceledException)
            {
                // Navigation away or filter changed 鈥?not an error
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StatsPage] LoadRecentBattlesAsync failed: {ex}");
            }
            finally
            {
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

                if (stats?.Code != 200 || stats.Data == null) return;

                var data = stats.Data;

                if (data.Grade != null)
                {
                    RankName = data.Grade.GradeName ?? string.Empty;
                    RankIcon = data.Grade.GradeIcon ?? string.Empty;
                    RankScore = data.Grade.GradeScore;
                    RankLevel = data.Grade.GradeLevel ?? string.Empty;
                    PageRankName = GetRankNameForScore(data.Grade.GradeScore, (int)gameMode);
                    PageStarCount = GetStarCount(data.Grade.GradeScore, (int)gameMode);
                    PageHasStars = IsTianxuanMode((int)gameMode) && data.Grade.GradeScore >= 4500;
                    RankDisplayWithStars = FormatPageRankDisplay(data.Grade.GradeScore, (int)gameMode);
                    RankTierScore = GetRankTierScore(data.Grade.GradeScore, (int)gameMode);
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
                System.Diagnostics.Debug.WriteLine($"[StatsPage] FetchAndUpdateHonorTitlesAsync failed: {ex}");
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
                if (score >= 7000) return "无量梵天";
                if (score >= 6000) return "无相龙王";
                if (score >= 5000) return "无双修罗";
                if (score >= 4500) return "无间修罗";
                if (score >= 4000) return "坠日";
                if (score >= 3500) return "蚀月";
                if (score >= 3000) return "陨星";
                if (score >= 2500) return "铂金";
                if (score >= 2000) return "黄金";
                if (score >= 1500) return "白银";
                return "青铜";
            }
            else
            {
                if (score >= 7000) return "无间泰斗";
                if (score >= 6500) return "御天尊者";
                if (score >= 6000) return "劫虚圣主";
                if (score >= 5500) return "穹苍魁首";
                if (score >= 5000) return "日曜名宿";
                if (score >= 4500) return "星月宗师";
                if (score >= 4000) return "云霄武圣";
                if (score >= 3500) return "绝顶高手";
                if (score >= 3000) return "凡尘武师";
                return "凡尘武师";
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
        public string Desc { get; set; } = string.Empty;
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
