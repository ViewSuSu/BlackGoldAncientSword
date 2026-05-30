using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Modules.UI.Stats.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels
{
    public class TeamInfoPageViewModel : ViewModelBase
    {
        private readonly IGameStatusMonitor _gameStatusMonitor;
        private readonly ITeamInfoOcrService _teamInfoOcrService;
        private readonly IPlayerPrefsService _playerPrefsService;
        private CancellationTokenSource? _ocrLoopCts;
        private bool _isOcrRunning;
        private readonly object _ocrLock = new();
        private bool _isSubscribed;
        private CancellationTokenSource? _refreshMembersCts;

        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;

        public TeamInfoPageViewModel(
            IGameStatusMonitor gameStatusMonitor,
            ITeamInfoOcrService teamInfoOcrService,
            IPlayerPrefsService playerPrefsService)
        {
            _gameStatusMonitor = gameStatusMonitor;
            _teamInfoOcrService = teamInfoOcrService;
            _playerPrefsService = playerPrefsService;
            TeamMembers = new ObservableCollection<TeamMemberInfo>();
            Seasons = new ObservableCollection<SeasonInfo>();
            DiffLeft = new ObservableCollection<MemberDiffItem>();
            DiffRight = new ObservableCollection<MemberDiffItem>();
            _selectedTeamSize = TeamSize.Trio;
            _selectedCategory = GameModeCategory.Rank;
        }

        // === Filters ===
        public ObservableCollection<SeasonInfo> Seasons { get; }

        private SeasonInfo? _selectedSeason;
        public SeasonInfo? SelectedSeason
        {
            get => _selectedSeason;
            set
            {
                if (SetProperty(ref _selectedSeason, value))
                    RefreshTeamMemberData();
            }
        }

        private TeamSize _selectedTeamSize;
        public TeamSize SelectedTeamSize
        {
            get => _selectedTeamSize;
            set
            {
                if (SetProperty(ref _selectedTeamSize, value))
                    RefreshTeamMemberData();
            }
        }

        private GameModeCategory _selectedCategory;
        public GameModeCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    RefreshTeamMemberData();
            }
        }

        private DelegateCommand<TeamSizeOption>? _selectTeamSizeCommand;
        public DelegateCommand<TeamSizeOption> SelectTeamSizeCommand =>
            _selectTeamSizeCommand ??= new DelegateCommand<TeamSizeOption>(param =>
            {
                if (param != null) SelectedTeamSize = param.Value;
            });

        private DelegateCommand<GameModeCategoryOption>? _selectCategoryCommand;
        public DelegateCommand<GameModeCategoryOption> SelectCategoryCommand =>
            _selectCategoryCommand ??= new DelegateCommand<GameModeCategoryOption>(param =>
            {
                if (param != null) SelectedCategory = param.Value;
            });

        public static System.ComponentModel.BindingList<TeamSizeOption> TeamSizes { get; } =
            new(new[] { new TeamSizeOption(TeamSize.Trio), new TeamSizeOption(TeamSize.Duo), new TeamSizeOption(TeamSize.Solo) });

        public static System.ComponentModel.BindingList<GameModeCategoryOption> Categories { get; } =
            new(new[] { new GameModeCategoryOption(GameModeCategory.Rank), new GameModeCategoryOption(GameModeCategory.Match), new GameModeCategoryOption(GameModeCategory.Tianren) });

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

        // === Members ===
        public ObservableCollection<TeamMemberInfo> TeamMembers { get; }

        public TeamMemberInfo? Member0 => TeamMembers.Count > 0 ? TeamMembers[0] : null;
        public TeamMemberInfo? Member1 => TeamMembers.Count > 1 ? TeamMembers[1] : null;
        public TeamMemberInfo? Member2 => TeamMembers.Count > 2 ? TeamMembers[2] : null;

        public bool IsWaiting => TeamMembers.Count == 0;
        public bool HasMember0 => TeamMembers.Count > 0;
        public bool HasMember1 => TeamMembers.Count > 1;
        public bool HasMember2 => TeamMembers.Count > 2;
        public bool HasThreeMembers => TeamMembers.Count >= 3;

        // === Diffs ===
        public ObservableCollection<MemberDiffItem> DiffLeft { get; }
        public ObservableCollection<MemberDiffItem> DiffRight { get; }
        private static bool MemberHasData(TeamMemberInfo m) =>
            !string.IsNullOrEmpty(m.UID) && m.Stats.Count > 0;

        public bool HasDiffLeft => TeamMembers.Count >= 2 && MemberHasData(TeamMembers[0]) && MemberHasData(TeamMembers[1]);
        public bool HasDiffRight => TeamMembers.Count >= 3 && MemberHasData(TeamMembers[1]) && MemberHasData(TeamMembers[2]);

        public System.Windows.GridLength Col0Width => HasMember0 ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(0);
        public System.Windows.GridLength Col1Width => HasDiffLeft ? new System.Windows.GridLength(60) : new System.Windows.GridLength(0);
        public System.Windows.GridLength Col2Width => HasMember1 ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(0);
        public System.Windows.GridLength Col3Width => HasDiffRight ? new System.Windows.GridLength(60) : new System.Windows.GridLength(0);
        public System.Windows.GridLength Col4Width => HasMember2 ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(0);

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _statusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private async void OnGameStatusRecognized(object? sender, GameStatusChangedEventArgs args)
        {
            switch (args.Status)
            {
                case GameStatus.HeroSelection:
                    StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                    StartOcrLoop();
                    break;
                case GameStatus.InGame:
                    StopOcrLoop();
                    break;
                case GameStatus.BattleEnded:
                case GameStatus.Unknown:
                    StopOcrLoop();
                    StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
                    TeamMembers.Clear();
                    break;
            }
        }

        private void StartOcrLoop()
        {
            lock (_ocrLock)
            {
                if (_isOcrRunning) return;
                _isOcrRunning = true;
                CancelAndDispose(ref _ocrLoopCts);
                _ocrLoopCts = new CancellationTokenSource();
            }
            var ct = _ocrLoopCts!.Token;
            _ = OcrLoopAsync(ct);
        }

        private void StopOcrLoop()
        {
            lock (_ocrLock)
            {
                if (!_isOcrRunning) return;
                _isOcrRunning = false;
                CancelAndDispose(ref _ocrLoopCts);
            }
        }

        private async Task OcrLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var names = await _teamInfoOcrService.RecognizeTeamMembersAsync(ct);
                    if (names.Length > 0 && !ct.IsCancellationRequested)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateTeamMembersAsync(names, ct);
                        });
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TeamInfo] OCR loop error: {ex.Message}");
                }

                try { await Task.Delay(1500, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task UpdateTeamMembersAsync(string[] names, CancellationToken ct)
        {
            var existingNames = TeamMembers.Select(m => m.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newNames = names.Where(n => !existingNames.Contains(n)).ToArray();

            var recognizedSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = TeamMembers.Where(m => !recognizedSet.Contains(m.UserName)).ToList();
            foreach (var r in removed)
                TeamMembers.Remove(r);

            foreach (var name in newNames)
            {
                ct.ThrowIfCancellationRequested();
                var member = new TeamMemberInfo { UserName = name, IsLoading = true, RefreshAction = RefreshSingleMember };
                TeamMembers.Add(member);
                _ = LoadMemberDataAsync(member, ct);
            }

            ReorderMembersForLocalUser();
            UpdateDiffs();
            IsLoading = TeamMembers.Any(m => m.IsLoading);
            RaiseMemberProperties();
        }

        private void ReorderMembersForLocalUser()
        {
            if (TeamMembers.Count < 2) return;
            var localName = _playerPrefsService.Current.OriginalPlayerName;
            if (string.IsNullOrEmpty(localName)) return;

            var list = TeamMembers.ToList();
            var localIdx = list.FindIndex(m =>
                string.Equals(m.UserName, localName, StringComparison.OrdinalIgnoreCase));
            if (localIdx < 0) return;

            // For 3 members: local user goes to center (index 1)
            if (TeamMembers.Count == 3)
            {
                if (localIdx != 1)
                {
                    TeamMembers.Move(localIdx, 1);
                }
            }
            // For 2 members: local user goes to left (index 0)
            else if (TeamMembers.Count == 2)
            {
                if (localIdx != 0)
                {
                    TeamMembers.Move(localIdx, 0);
                }
            }
        }

        private int LocalUserIndex
        {
            get
            {
                var localName = _playerPrefsService.Current.OriginalPlayerName;
                if (string.IsNullOrEmpty(localName)) return 0;
                return TeamMembers.ToList().FindIndex(m =>
                    string.Equals(m.UserName, localName, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void UpdateDiffs()
        {
            DiffLeft.Clear();
            DiffRight.Clear();

            var localIdx = LocalUserIndex;
            if (localIdx < 0) localIdx = 0;

            if (TeamMembers.Count >= 2)
            {
                var otherIdx = localIdx == 0 ? 1 : 0;
                ComputeDiff(DiffLeft, TeamMembers[localIdx], TeamMembers[otherIdx]);
            }

            if (TeamMembers.Count >= 3)
            {
                var otherIdx = localIdx == 1 ? 2 : 1;
                ComputeDiff(DiffRight, TeamMembers[localIdx], TeamMembers[otherIdx]);
            }

            RaisePropertyChanged(nameof(HasDiffLeft));
            RaisePropertyChanged(nameof(HasDiffRight));
            RaisePropertyChanged(nameof(Col1Width));
            RaisePropertyChanged(nameof(Col3Width));
        }

                private static readonly (string Key, string Label, bool IsPercent)[] StatDefs =
        {
            ("avg_kill", "场均击杀", false),
            ("avg_damage", "场均伤害", false),
            ("top5_rate", "前五率", true),
            ("avg_total_live_time", "场均生存", false),
            ("kd", "KD", false),
            ("avg_cure", "场均治疗", false),
            ("avg_assist", "场均助攻", false),
            ("max_kill", "最佳击杀", false),
            ("max_damage", "最佳伤害", false),
            ("max_shock_count", "最多振刀", false),
            ("win_rate", "第一率", true),
            ("round", "场次", false),
            ("win", "第一", false),
            ("top5", "前五", false),
            ("max_cure", "最佳治疗", false),
            ("max_assist", "最佳助攻", false),
        };

        private static void ComputeDiff(ObservableCollection<MemberDiffItem> target, TeamMemberInfo left, TeamMemberInfo right)
        {
            foreach (var def in StatDefs)
            {
                var lv = left.Stats.TryGetValue(def.Key, out var l) ? TryParseDouble(l) : 0;
                var rv = right.Stats.TryGetValue(def.Key, out var r) ? TryParseDouble(r) : 0;
                AddDiffItem(target, def.Label, lv, rv, def.IsPercent);
            }
            AddDiffItem(target, "段位分", left.RankScore, right.RankScore, false);
        }

        private static void AddDiffItem(ObservableCollection<MemberDiffItem> target, string label, double leftVal, double rightVal, bool isPercent)
        {
            var diff = leftVal - rightVal;
            string diffText;
            string color;
            if (Math.Abs(diff) < 0.001)
            {
                diffText = "0";
                color = "#999999";
            }
            else if (diff > 0)
            {
                diffText = isPercent ? $"+{diff:F1}%" : $"+{diff:F0}";
                color = "#22AA22";
            }
            else
            {
                diffText = isPercent ? $"{diff:F1}%" : $"{diff:F0}";
                color = "#DD3333";
            }

            target.Add(new MemberDiffItem
            {
                Label = label,
                LeftValue = isPercent ? $"{leftVal:F1}%" : $"{leftVal:F0}",
                RightValue = isPercent ? $"{rightVal:F1}%" : $"{rightVal:F0}",
                DiffText = diffText,
                DiffColor = color,
                IsLeftBetter = diff > 0.001
            });
        }

        private static double TryParseDouble(string s)
        {
            if (double.TryParse(s?.Replace("%", ""), out var v)) return v;
            return 0;
        }

        public void RefreshSingleMember(TeamMemberInfo member)
        {
            member.IsLoading = true;
            member.StatusText = "";
            var cts = new CancellationTokenSource();
            _ = LoadMemberDataAsync(member, cts.Token);
        }

        private async Task LoadMemberDataAsync(TeamMemberInfo member, CancellationToken ct)
        {
            try
            {
                var search = await NarakaApiClient.SearchRecordAsync(member.UserName, ct);
                if (search?.Data == null || string.IsNullOrEmpty(search.Data.RoleIdSimple))
                {
                    member.StatusText = L("TeamInfo.PlayerNotFound", "未找到该玩家");
                    member.IsLoading = false;
                    return;
                }

                var roleId = search.Data.RoleIdSimple;

                var userInfo = await NarakaApiClient.GetUserInfoAsync(roleId, ct);
                if (userInfo?.Code != 200 || userInfo.Data == null)
                {
                    member.StatusText = L("TeamInfo.LoadFailed", "加载失败");
                    member.IsLoading = false;
                    return;
                }

                var d = userInfo.Data;
                member.UserName = d.Role?.RoleName ?? d.NickName ?? member.UserName;
                member.Level = $"Lv.{d.Role?.RoleLevel ?? 0}";
                member.UID = d.Role?.Uid ?? string.Empty;
                member.AvatarUrl = d.Role?.HeadIcon ?? string.Empty;
                member.SoloRankScore = d.SurviveSingleGrade;
                member.DuoRankScore = d.SurviveDoubleGrade;
                member.TrioRankScore = d.SurviveTriplexGrade;

                var seasonId = _selectedSeason?.Code ?? d.CurrentSeasonId;
                var gameMode = ResolveGameMode(_selectedCategory, _selectedTeamSize);
                var stats = await NarakaApiClient.GetPlayerStatsAsync(roleId, seasonId, gameMode, ct);
                if (stats?.Code == 200 && stats.Data?.Stats != null)
                {
                    member.Stats.Clear();
                    foreach (var stat in stats.Data.Stats)
                    {
                        if (stat.Key == null) continue;
                        var val = stat.Value ?? "-";
                        member.Stats[stat.Key] = val;
                        switch (stat.Key)
                        {
                            case "avg_kill": member.KillCount = val; break;
                            case "top5_rate": member.Top5Rate = val; break;
                            case "avg_damage": member.DamagePlayer = val; break;
                            case "avg_total_live_time":
                                member.SurviveTime = FormatSurvivalTime(val);
                                break;
                        }
                    }

                    if (stats.Data.Grade != null)
                    {
                        member.RankName = stats.Data.Grade.GradeName ?? string.Empty;
                        member.RankIcon = stats.Data.Grade.GradeIcon ?? string.Empty;
                        member.RankScore = stats.Data.Grade.GradeScore;
                        var gm = (int)gameMode;
                        member.PageRankName = GetRankNameForScore(stats.Data.Grade.GradeScore, gm);
                        member.PageStarCount = GetStarCount(stats.Data.Grade.GradeScore, gm);
                        member.PageHasStars = IsTianxuanMode(gm) && stats.Data.Grade.GradeScore >= 4500;
                        member.RankTierScore = GetRankTierScore(stats.Data.Grade.GradeScore, gm);
                    }
                }

                member.StatusText = string.Empty;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamInfo] Load member error: {ex.Message}");
                member.StatusText = L("TeamInfo.LoadError", "加载出错");
            }
            finally
            {
                member.IsLoading = false;
                UpdateDiffs();
                IsLoading = TeamMembers.Any(m => m.IsLoading);
                RaiseMemberProperties();
            }
        }

        private void RefreshTeamMemberData()
        {
            CancelAndDispose(ref _refreshMembersCts);
            _refreshMembersCts = new CancellationTokenSource();
            var ct = _refreshMembersCts.Token;
            _ = RefreshMembersAsync(ct);
        }

        private async Task RefreshMembersAsync(CancellationToken ct)
        {
            var members = TeamMembers.ToList();
            var tasks = new List<Task>();
            foreach (var member in members)
            {
                if (string.IsNullOrEmpty(member.UID)) continue;
                member.IsLoading = true;
                tasks.Add(Task.Run(async () =>
                {
                    try { await LoadMemberDataAsync(member, ct); }
                    finally { member.IsLoading = false; }
                }, ct));
            }
            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
            UpdateDiffs();
            IsLoading = TeamMembers.Any(m => m.IsLoading);
            RaiseMemberProperties();
        }

        private void RaiseMemberProperties()
        {
            RaisePropertyChanged(nameof(IsWaiting));
            RaisePropertyChanged(nameof(Member0));
            RaisePropertyChanged(nameof(Member1));
            RaisePropertyChanged(nameof(Member2));
            RaisePropertyChanged(nameof(HasMember0));
            RaisePropertyChanged(nameof(HasMember1));
            RaisePropertyChanged(nameof(HasMember2));
            RaisePropertyChanged(nameof(HasThreeMembers));
            RaisePropertyChanged(nameof(Col0Width));
            RaisePropertyChanged(nameof(Col1Width));
            RaisePropertyChanged(nameof(Col2Width));
            RaisePropertyChanged(nameof(Col3Width));
            RaisePropertyChanged(nameof(Col4Width));
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            cts.Dispose();
            cts = null;
        }

        private static bool IsTianxuanMode(int gameMode) =>
            gameMode == 1 || gameMode == 12 || gameMode == 2;

        private static string GetRankNameForScore(int score, int gameMode = 0)
        {
            if (IsTianxuanMode(gameMode))
            {
                if (score >= 7000) return "\u65e0\u53cc\u4fee\u7f57";
                if (score >= 6000) return "\u65e0\u76f8\u9f99\u738b";
                if (score >= 5000) return "\u65e0\u538c\u4fee\u7f57";
                if (score >= 4500) return "\u65e0\u95f4\u4fee\u7f57";
                if (score >= 4000) return "\u5760\u65e5";
                if (score >= 3500) return "\u8680\u6708";
                if (score >= 3000) return "\u9668\u661f";
                if (score >= 2500) return "\u94c2\u91d1";
                if (score >= 2000) return "\u9ec4\u91d1";
                if (score >= 1500) return "\u767d\u94f6";
                return "\u9752\u94dc";
            }
            else
            {
                if (score >= 7000) return "\u65e0\u95f4\u6cf0\u6597";
                if (score >= 6500) return "\u5fa1\u5929\u5c0a\u8005";
                if (score >= 6000) return "\u52ab\u865a\u5723\u4e3b";
                if (score >= 5500) return "\u7a79\u82cd\u9b42\u9996";
                if (score >= 5000) return "\u65e5\u66dc\u540d\u5bbf";
                if (score >= 4500) return "\u661f\u6708\u5b97\u5e08";
                if (score >= 4000) return "\u4e91\u96fe\u6b66\u5723";
                if (score >= 3500) return "\u9876\u7ea7\u9ad8\u624b";
                if (score >= 3000) return "\u51e1\u5c18\u6b66\u5e08";
                return "\u51e1\u5c18\u6b66\u5e08";
            }
        }

        private static int GetStarCount(int score, int gameMode = 0)
        {
            if (!IsTianxuanMode(gameMode)) return 0;
            // For ??+ (>=4500): accumulated stars (floor)
            if (score >= 7000) return (score - 7000) / 100;
            if (score >= 6000) return (score - 6000) / 100;
            if (score >= 5000) return (score - 5000) / 100;
            if (score >= 4500) return (score - 4500) / 100;
            // For ranks below ??: remaining stars to next rank (ceil)
            int[] thresholds = { 4500, 4000, 3500, 3000, 2500, 2000, 1500, 0 };
            for (int t = 0; t < thresholds.Length - 1; t++)
            {
                if (score >= thresholds[t + 1])
                {
                    var remaining = thresholds[t] - score;
                    return (remaining + 99) / 100; // ceil division
                }
            }
            return 0;
        }

        private static string FormatSurvivalTime(string secondsStr)
        {
            if (double.TryParse(secondsStr, out double seconds))
            {
                var minutes = (int)(seconds / 60);
                var remainSeconds = (int)(seconds % 60);
                return $"{minutes}分{remainSeconds:D2}秒";
            }
            return secondsStr;
        }

        private static int GetRankTierScore(int score, int gameMode = 0)
        {
            if (!IsTianxuanMode(gameMode)) return score;
            if (score >= 7000) return (score - 7000) % 100;
            if (score >= 6000) return (score - 6000) % 100;
            if (score >= 5000) return (score - 5000) % 100;
            if (score >= 4500) return (score - 4500) % 100;
            if (score >= 4000) return (score - 4000) % 100;
            if (score >= 3500) return (score - 3500) % 100;
            if (score >= 3000) return (score - 3000) % 100;
            if (score >= 2500) return (score - 2500) % 100;
            if (score >= 2000) return (score - 2000) % 100;
            if (score >= 1500) return (score - 1500) % 100;
            return score % 100;
        }

        protected override async void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);

            if (!_isSubscribed)
            {
                _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;
                _isSubscribed = true;
            }

            _ = LoadSeasonsAsync();

            if (_gameStatusMonitor.CurrentStatus == GameStatus.HeroSelection)
            {
                StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                StartOcrLoop();
            }
            else
            {
                if (_gameStatusMonitor.CurrentStatus != GameStatus.InGame)
                    StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
            }
        }

        private async Task LoadSeasonsAsync()
        {
            try
            {
                var seasonsResp = await NarakaApiClient.QuerySeasonsAsync();
                if (seasonsResp?.Data != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Seasons.Clear();
                        foreach (var s in seasonsResp.Data)
                            Seasons.Add(s);
                        if (Seasons.Count > 0 && _selectedSeason == null)
                            _selectedSeason = Seasons[0];
                        RaisePropertyChanged(nameof(SelectedSeason));
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamInfo] Load seasons error: {ex.Message}");
            }
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            StopOcrLoop();
            CancelAndDispose(ref _refreshMembersCts);
            if (_isSubscribed)
            {
                _gameStatusMonitor.GameStatusRecognized -= OnGameStatusRecognized;
                _isSubscribed = false;
            }
            base.OnNavigatedFromExecute(navigationContext);
        }
    }

    public class MemberDiffItem
    {
        public string Label { get; set; } = string.Empty;
        public string LeftValue { get; set; } = string.Empty;
        public string RightValue { get; set; } = string.Empty;
        public string DiffText { get; set; } = string.Empty;
        public string DiffColor { get; set; } = "#999999";
        public bool IsLeftBetter { get; set; }
    }

    public class TeamMemberInfo : ViewModelBase
    {
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

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (SetProperty(ref _statusText, value))
                    RaisePropertyChanged(nameof(HasStatusError));
            }
        }

        public bool HasStatusError => !string.IsNullOrEmpty(_statusText);

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

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

        private int _rankTierScore;
        public int RankTierScore
        {
            get => _rankTierScore;
            set => SetProperty(ref _rankTierScore, value);
        }

        private int _soloRankScore;
        public int SoloRankScore
        {
            get => _soloRankScore;
            set => SetProperty(ref _soloRankScore, value);
        }

        private int _duoRankScore;
        public int DuoRankScore
        {
            get => _duoRankScore;
            set => SetProperty(ref _duoRankScore, value);
        }

        private int _trioRankScore;
        public int TrioRankScore
        {
            get => _trioRankScore;
            set => SetProperty(ref _trioRankScore, value);
        }

        // Stats dictionary: key -> display value
        public System.Collections.Generic.Dictionary<string, string> Stats { get; } = new();

        private string _killCount = string.Empty;
        public string KillCount
        {
            get => _killCount;
            set => SetProperty(ref _killCount, value);
        }

        private string _top5Rate = string.Empty;
        public string Top5Rate
        {
            get => _top5Rate;
            set => SetProperty(ref _top5Rate, value);
        }

        private string _damagePlayer = string.Empty;
        public string DamagePlayer
        {
            get => _damagePlayer;
            set => SetProperty(ref _damagePlayer, value);
        }

        private string _surviveTime = string.Empty;
        public string SurviveTime
        {
            get => _surviveTime;
            set => SetProperty(ref _surviveTime, value);
        }

        public System.Action<TeamMemberInfo>? RefreshAction { get; set; }

        private DelegateCommand? _searchMemberCommand;
        public DelegateCommand SearchMemberCommand =>
            _searchMemberCommand ??= new DelegateCommand(() =>
            {
                RefreshAction?.Invoke(this);
            });

        private DelegateCommand? _copyUserNameCommand;
        public DelegateCommand CopyUserNameCommand =>
            _copyUserNameCommand ??= new DelegateCommand(() =>
            {
                System.Windows.Clipboard.SetText(UserName);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(
                        System.Windows.Application.Current?.TryFindResource("Stats.CopySuccess") as string ?? "\u590d\u5236\u6210\u529f"));
            });

        private DelegateCommand? _copyUIDCommand;
        public DelegateCommand CopyUIDCommand =>
            _copyUIDCommand ??= new DelegateCommand(() =>
            {
                System.Windows.Clipboard.SetText(UID);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(
                        System.Windows.Application.Current?.TryFindResource("Stats.CopySuccess") as string ?? "\u590d\u5236\u6210\u529f"));
            });
    }
}
