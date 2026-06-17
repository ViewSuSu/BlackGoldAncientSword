using System.Collections.ObjectModel;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using System.Linq;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.Stats.ViewModels;
using BlackGoldAncientSword.Framework.UI.Controls;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels
{
    public class TeamInfoPageViewModel : ViewModelBase
    {
        private readonly IGameStatusMonitor _gameStatusMonitor;
        private readonly IPlayerPrefsService _playerPrefsService;
        private readonly IMainContentNavigationService _navigation;
        private readonly ITeamOverlayService _teamOverlayService;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly IClipboardService _clipboard;
        private readonly TeamOcrCoordinator _ocrCoordinator;
        private readonly TeamMemberLoader _memberLoader;
        private readonly ILocalizedTextProvider _localizedText;
        private CancellationTokenSource? _ocrLoopCts;
        private bool _isOcrRunning;
        private readonly object _ocrLock = new();
        private bool _isHeroSelectionPhase;
        private CancellationTokenSource? _refreshMembersCts;
        private CancellationTokenSource? _refreshOcrCts;
        private bool _hasEverHadData;
        private bool _overlayShownForThisRound;
        private bool _overlayDismissedThisRound;

        // 通过 ILocalizedTextProvider 访问字符串本地化资源，避免 VM 直接依赖 System.Windows.Application。
        private string L(string key, string fallback) => _localizedText.Get(key, fallback);

        public TeamInfoPageViewModel(
            IGameStatusMonitor gameStatusMonitor,
            IPlayerPrefsService playerPrefsService,
            ITeamOverlayService teamOverlayService,
            IMainContentNavigationService navigation,
            IUIDispatcher uiDispatcher,
            IClipboardService clipboard,
            TeamOcrCoordinator ocrCoordinator,
            TeamMemberLoader memberLoader,
            ILocalizedTextProvider localizedText)
        {
            _gameStatusMonitor = gameStatusMonitor;
            _teamOverlayService = teamOverlayService;
            _playerPrefsService = playerPrefsService;
            _navigation = navigation;
            _uiDispatcher = uiDispatcher;
            _clipboard = clipboard;
            _ocrCoordinator = ocrCoordinator;
            _memberLoader = memberLoader;
            _localizedText = localizedText;

            TeamMembers = new ObservableCollection<TeamMemberInfo>();
            Seasons = new ObservableCollection<SeasonInfo>();
            DiffLeft = new ObservableCollection<MemberDiffItem>();
            DiffRight = new ObservableCollection<MemberDiffItem>();
            _selectedTeamSize = TeamSize.Trio;
            _selectedCategory = GameModeCategory.Rank;
            // _statusText 不能用字段初始化器调 L()，因为 _localizedText 此时还未注入。
            _statusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
        }

        // === Filters ===
        public ObservableCollection<SeasonInfo> Seasons { get; }

        private SeasonInfo? _selectedSeason;
        public SeasonInfo? SelectedSeason
        {
            get => _selectedSeason;
            set
            {
                if (_selectedSeason == value) return;
                _selectedSeason = value;
                RaisePropertyChanged(nameof(SelectedSeason));
                RefreshTeamMemberData();
            }
        }

        private TeamSize _selectedTeamSize;
        public TeamSize SelectedTeamSize
        {
            get => _selectedTeamSize;
            set
            {
                if (_selectedTeamSize == value) return;
                _selectedTeamSize = value;
                RaisePropertyChanged(nameof(SelectedTeamSize));
                RefreshTeamMemberData();
            }
        }

        private GameModeCategory _selectedCategory;
        public GameModeCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory == value) return;
                _selectedCategory = value;
                RaisePropertyChanged(nameof(SelectedCategory));
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

        private DelegateCommand? _refreshOcrCommand;
        public DelegateCommand RefreshOcrCommand =>
            _refreshOcrCommand ??= new DelegateCommand(async () =>
            {
                await RefreshOcrOnceAsync();
            });

        public static System.ComponentModel.BindingList<TeamSizeOption> TeamSizes { get; } =
            new(new[] { new TeamSizeOption(TeamSize.Trio), new TeamSizeOption(TeamSize.Duo), new TeamSizeOption(TeamSize.Solo) });

        public static System.ComponentModel.BindingList<GameModeCategoryOption> Categories { get; } =
            new(new[] { new GameModeCategoryOption(GameModeCategory.Rank), new GameModeCategoryOption(GameModeCategory.Match), new GameModeCategoryOption(GameModeCategory.Tianren) });


        // === Members ===
        public ObservableCollection<TeamMemberInfo> TeamMembers { get; }

        public TeamMemberInfo? Member0 => TeamMembers.Count > 0 ? TeamMembers[0] : null;
        public TeamMemberInfo? Member1 => TeamMembers.Count > 1 ? TeamMembers[1] : null;
        public TeamMemberInfo? Member2 => TeamMembers.Count > 2 ? TeamMembers[2] : null;

        public bool IsWaiting => TeamMembers.Count == 0 && !_hasEverHadData;
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

        // ColXWidth (GridLength) 属性已移除：View code-behind 改为监听 HasMember*/HasDiff* 五个 bool
        // 直接构造 GridLength 写入 ColumnDefinitions[].Width，VM 因此不再依赖 System.Windows 类型。

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
            }
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        public bool IsHeroSelectionPhase
        {
            get => _isHeroSelectionPhase;
            set
            {
                if (_isHeroSelectionPhase == value) return;
                _isHeroSelectionPhase = value;
                RaisePropertyChanged(nameof(IsHeroSelectionPhase));
            }
        }

        private void OnGameStatusRecognized(object? sender, GameStatusChangedEventArgs args)
        {
            // GameStatusRecognized 可能从 ThreadPool 触发（GameLogMonitor 的 FileSystemWatcher.OnLogChanged
            // 走 Task.Run → 同步 raise BattleStarted/Ended/Joined → MainWindowViewModel/HomePageViewModel
            // 同步调 _gameStatusMonitor.NotifyStatus → 同步 raise GameStatusRecognized）。
            // 下方 HandleGameStatusOnUiThread 会修改 ObservableCollection（TeamMembers.Clear/DiffLeft.Clear/DiffRight.Clear），
            // ObservableCollection.CollectionChanged 跨线程触发会让 WPF ItemsControl 抛
            // NotSupportedException 撕崩 UI 线程。务必在方法入口 marshal 回 UI 线程。
            if (!_uiDispatcher.CheckAccess())
            {
                _ = _uiDispatcher.InvokeAsync(() => HandleGameStatusOnUiThread(sender, args));
                return;
            }

            HandleGameStatusOnUiThread(sender, args);
        }

        private void HandleGameStatusOnUiThread(object? sender, GameStatusChangedEventArgs args)
        {
            switch (args.Status)
            {
                case GameStatus.HeroSelection:
                    IsHeroSelectionPhase = true;
                    _overlayShownForThisRound = false;
                    StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                    StartOcrLoop();
                    break;
                case GameStatus.InGame:
                    IsHeroSelectionPhase = false;
                    _teamOverlayService.Hide();
                    CancelAndDispose(ref _refreshOcrCts);
                    break;
                case GameStatus.Unknown:
                    IsHeroSelectionPhase = false;
                    _teamOverlayService.Hide();
                    CancelAndDispose(ref _refreshOcrCts);
                    StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
                    if (TeamMembers.Count > 0)
                    {
                        _hasEverHadData = false;
                        TeamMembers.Clear();
                        DiffLeft.Clear();
                        DiffRight.Clear();
                        RaiseMemberProperties();
                    }
                    break;
            }
        }

        private void StartOcrLoop()
        {
            // Token 必须在 lock 内取出：单 UI 线程下虽然 Stop/Start 不会并发，
            // 但写成 lock 外读 `_ocrLoopCts!.Token` 会让"如果有人并发 Stop"
            // 的场景命中 NullReferenceException（CancelAndDispose 把 ref 置 null）。
            // 在 lock 内取出 CancellationToken（struct），离 lock 后即可安全使用。
            CancellationToken ct;
            lock (_ocrLock)
            {
                if (_isOcrRunning) return;
                _isOcrRunning = true;
                CancelAndDispose(ref _ocrLoopCts);
                _ocrLoopCts = new CancellationTokenSource();
                ct = _ocrLoopCts.Token;
            }
            _ = OcrLoopAsync(ct);
        }

        private void OnOverlayDismissed()
        {
            _overlayDismissedThisRound = true;
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

        private async Task RefreshOcrOnceAsync()
        {
            // 取消之前的刷新操作
            CancelAndDispose(ref _refreshOcrCts);
            _refreshOcrCts = new CancellationTokenSource();
            var ct = _refreshOcrCts.Token;

            _overlayShownForThisRound = false;
            try
            {
                StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");

                var names = await _ocrCoordinator.RecognizeOnceAsync(ct);
                ct.ThrowIfCancellationRequested();

                if (names.Length == 0) return;

                // 在 UI 线程上更新成员列表
                await _uiDispatcher.InvokeAsync(() =>
                {
                    _hasEverHadData = true;

                    var recognizedSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var removed = TeamMembers.Where(m => !recognizedSet.Contains(m.UserName)).ToList();
                    foreach (var r in removed)
                        TeamMembers.Remove(r);

                    var existingNames = TeamMembers.Select(m => m.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in names)
                    {
                        if (existingNames.Contains(name)) continue;

                        var member = new TeamMemberInfo(_clipboard, _localizedText)
                        {
                            UserName = name,
                            IsLoading = true,
                            RefreshAction = RefreshSingleMember,
                            NavigateToStatsAction = username =>
                            {
                                _playerPrefsService.Current.PlayerName = username;
                                _navigation.NavigateTo(PageNames.StatsPage);
                            }
                        };
                        TeamMembers.Add(member);
                    }

                    // 强制所有成员进入加载状态（无论之前是否正在加载）
                    foreach (var member in TeamMembers)
                    {
                        member.IsLoading = true;
                        member.StatusText = string.Empty;
                    }

                    ReorderMembersForLocalUser();
                    RaiseMemberProperties();
                });

                // 从 UI 线程启动所有 LoadMemberDataAsync，确保 SynchronizationContext 正确
                var tasks = new List<Task>();
                await _uiDispatcher.InvokeAsync(() =>
                {
                    foreach (var member in TeamMembers)
                    {
                        tasks.Add(LoadMemberDataAsync(member, ct));
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[TeamInfo] Refresh OCR cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamInfo] Refresh OCR error: {ex.Message}");
            }
        }


        /// <summary>
        /// 覆盖层弹窗请求刷新时的回调。重置覆盖层标志后走完整的 OCR 刷新流程，
        /// 刷新完成后覆盖层会自动重新显示。
        /// <para>
        /// **async void 安全契约**：本方法被 ITeamOverlayService.RefreshAction (Action 类型) 持有调用，
        /// 必须保持 async void 签名。<see cref="RefreshOcrOnceAsync"/> 内部完整 try/catch 兜底所有 await 异常，
        /// 修改时**不得把抛出语义移出该 try/catch**——否则异常会直接冲到 SynchronizationContext，
        /// 由 App.xaml.cs 的 DispatcherUnhandledException 接管并向用户弹错。
        /// 如需返回 Task，必须同步把 RefreshAction 类型改为 Func&lt;Task&gt; 并审计所有调用方。
        /// </para>
        /// </summary>
        private async void RefreshFromOverlay()
        {
            try
            {
                _overlayShownForThisRound = false;
                await RefreshOcrOnceAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(TeamInfoPageViewModel)}.{nameof(RefreshFromOverlay)}] {ex}");
            }
        }

        private async Task OcrLoopAsync(CancellationToken ct)
        {
            try
            {
                // retryInterval=Zero 时会变成"识别失败立刻重试"的零休眠死循环：
                // 一旦英雄选择阶段 OCR 拿不到队友名（例如分辨率不匹配、UI 还没绘出），
                // 每秒会反复触发 3 次 PaddleOCR 推理 + 全屏抓取，把 CPU 与磁盘打满。
                // 800 ms 在人类感知上等同立刻，但 CPU 占用立刻降一个数量级。
                var names = await _ocrCoordinator.WaitForFirstRecognitionAsync(
                    initialDelay: TimeSpan.FromSeconds(2),
                    retryInterval: TimeSpan.FromMilliseconds(800),
                    ct);

                if (ct.IsCancellationRequested || names.Length == 0) return;

                await _uiDispatcher.InvokeAsync(() =>
                {
                    // InvokeAsync 委托签名是 Action，无法 await。fire-and-forget 但通过 SafeUpdateTeamMembers
                    // 包一层 try/catch，避免 OperationCanceledException / 其他异常变成 UnobservedTaskException。
                    _ = SafeUpdateTeamMembers(names, ct);
                });

                // Wait for all member data to load, then navigate to TeamInfo
                while (TeamMembers.Any(m => m.IsLoading) && !ct.IsCancellationRequested)
                {
                    await Task.Delay(300, ct);
                }
                // 数据已在单例 ViewModel 中，保留已加载的数据，无需重新导航
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamInfo] OCR loop error: {ex.Message}");
            }
            finally
            {
                StopOcrLoop();
            }
        }

        /// <summary>
        /// fire-and-forget 包装：捕获 <see cref="UpdateTeamMembersAsync"/> 异常以避免
        /// UnobservedTaskException（OperationCanceledException 视为正常取消）。
        /// </summary>
        private async Task SafeUpdateTeamMembers(string[] names, CancellationToken ct)
        {
            try
            {
                await UpdateTeamMembersAsync(names, ct);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，无需上报
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(TeamInfoPageViewModel)}.{nameof(SafeUpdateTeamMembers)}] {ex}");
            }
        }

        private async Task UpdateTeamMembersAsync(string[] names, CancellationToken ct)
        {
            // Skip if recognized names are identical to current members
            if (names.Length == TeamMembers.Count &&
                names.All(n => TeamMembers.Any(m =>
                    string.Equals(m.UserName, n, StringComparison.OrdinalIgnoreCase))))
                return;

            // Mark that we have loaded data at least once
            _hasEverHadData = true;

            var existingNames = TeamMembers.Select(m => m.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newNames = names.Where(n => !existingNames.Contains(n)).ToArray();

            var recognizedSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = TeamMembers.Where(m => !recognizedSet.Contains(m.UserName)).ToList();
            foreach (var r in removed)
                TeamMembers.Remove(r);

            foreach (var name in newNames)
            {
                ct.ThrowIfCancellationRequested();
                var member = new TeamMemberInfo(_clipboard, _localizedText)
                {
                    UserName = name,
                    IsLoading = true,
                    RefreshAction = RefreshSingleMember,
                    NavigateToStatsAction = username =>
                    {
                        _playerPrefsService.Current.PlayerName = username;
                        _navigation.NavigateTo(PageNames.StatsPage);
                    }
                };
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
        }

        private static readonly (string Key, string Label, bool IsPercent, string Format)[] StatDefs =
        {
            ("avg_kill", "场均击杀", false, "F1"),
            ("avg_damage", "场均伤害", false, "F0"),
            ("top5_rate", "前五率", true, "F1"),
            ("avg_total_live_time", "场均生存", false, "F0"),
            ("kd", "KD", false, "F2"),
            ("avg_cure", "场均治疗", false, "F0"),
            ("avg_assist", "场均助攻", false, "F1"),
            ("max_kill", "最佳击杀", false, "F0"),
            ("max_damage", "最佳伤害", false, "F0"),
            ("max_shock_count", "最多振刀", false, "F0"),
            ("win_rate", "第一率", true, "F1"),
            ("round", "场次", false, "F0"),
            ("win", "第一", false, "F0"),
            ("top5", "前五", false, "F0"),
            ("max_cure", "最佳治疗", false, "F0"),
            ("max_assist", "最佳助攻", false, "F0"),
            ("__rank__", "分数", false, "F0"),
        };

        private static void ComputeDiff(ObservableCollection<MemberDiffItem> target, TeamMemberInfo left, TeamMemberInfo right)
        {
            foreach (var def in StatDefs)
            {
                double lv, rv;
                if (def.Key == "__rank__")
                {
                    lv = left.RankScore;
                    rv = right.RankScore;
                }
                else
                {
                    lv = left.Stats.TryGetValue(def.Key, out var l) ? TryParseDouble(l) : 0;
                    rv = right.Stats.TryGetValue(def.Key, out var r) ? TryParseDouble(r) : 0;
                }

                AddDiffItem(target, def.Label, lv, rv, def.IsPercent);
            }

        }

        private static void AddDiffItem(ObservableCollection<MemberDiffItem> target, string label, double leftVal, double rightVal, bool isPercent)
        {
            var diff = leftVal - rightVal;
            const string fmt = "0.##"; // at most 2 decimal places
            string diffText;
            string color;
            if (Math.Abs(diff) < 0.001)
            {
                diffText = "0";
                color = "#999999";
            }
            else if (diff > 0)
            {
                diffText = isPercent ? $"+{diff:F1}%" : $"+{diff.ToString(fmt)}";
                color = "#22AA22";
            }
            else
            {
                diffText = isPercent ? $"{diff:F1}%" : $"{diff.ToString(fmt)}";
                color = "#DD3333";
            }

            target.Add(new MemberDiffItem
            {
                Label = label,
                LeftValue = isPercent ? $"{leftVal:F1}%" : $"{leftVal.ToString(fmt)}",
                RightValue = isPercent ? $"{rightVal:F1}%" : $"{rightVal.ToString(fmt)}",
                DiffText = diffText,
                DiffColor = color,
                IsLeftBetter = diff > 0.001,
                DiffTooltip = diff.ToString()
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
            // 不创建 CTS：原实现的 cts 从未调 Cancel，仅作为 token 来源然后 Dispose，等于 dead code。
            // 单成员刷新场景目前无取消需求；如未来需要"刷新整个团队时取消正在进行的单成员刷新"，
            // 应改为引入 member-scoped token 或复用 _refreshMembersCts，而非孤立的占位 CTS。
            _ = LoadMemberDataAsync(member, CancellationToken.None);
        }

        private async Task LoadMemberDataAsync(TeamMemberInfo member, CancellationToken ct)
        {
            var userName = member.UserName;
            if (string.IsNullOrWhiteSpace(userName)) return;

            try
            {
                // Step 1-4: 调 Loader 在后台线程拉数据；返回 DTO 后回 UI 线程逐批写回属性。
                var loaded = await _memberLoader.LoadAsync(
                    userName,
                    _selectedSeason?.Code,
                    _selectedCategory,
                    _selectedTeamSize,
                    ct).ConfigureAwait(false);

                if (loaded.Failed)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        member.StatusText = L("TeamInfo.QueryFailed", "查询失败");
                    });
                    return;
                }

                // Step 6: Dispatch member properties in smaller batches so the UI thread
                // can service input/paint between batches instead of blocking on one big dispatch.
                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.UserName = loaded.UserName;
                    member.Level = loaded.Level;
                    member.UID = loaded.UID;
                    member.AvatarUrl = loaded.AvatarUrl;
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.SoloRankScore = loaded.SoloRankScore;
                    member.DuoRankScore = loaded.DuoRankScore;
                    member.TrioRankScore = loaded.TrioRankScore;
                    member.KillCount = loaded.Stats?.AvgKill ?? member.KillCount;
                    member.Top5Rate = loaded.Stats?.Top5Rate ?? member.Top5Rate;
                    member.DamagePlayer = loaded.Stats?.AvgDamage ?? member.DamagePlayer;
                    member.SurviveTime = loaded.Stats?.SurviveTime ?? member.SurviveTime;
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    var s = loaded.Stats;
                    member.RankName = s?.RankName ?? member.RankName;
                    member.RankIcon = s?.RankIcon ?? member.RankIcon;
                    member.RankScore = s?.RankScore ?? 0;
                    member.PageRankName = s?.PageRankName ?? member.PageRankName;
                    member.PageStarCount = s?.PageStarCount ?? 0;
                    member.PageHasStars = s?.PageHasStars ?? false;
                    member.RankTierScore = s?.RankTierScore ?? 0;
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.Stats.Clear();
                    if (loaded.Stats != null)
                    {
                        foreach (var kv in loaded.Stats.Stats)
                            member.Stats[kv.Key] = kv.Value;
                    }
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.StatusText = "";
                });
            }
            catch (OperationCanceledException)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.IsLoading = false;
                });
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TeamInfo] Load member error: " + ex.Message);
                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.StatusText = L("TeamInfo.QueryFailed", "查询失败");
                });
            }
            // Final cleanup: dispatch UI updates in smaller batches so the UI thread
            // can service input/paint between batches instead of blocking on one big dispatch.
            try
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.IsLoading = false;
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    UpdateDiffs();
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    IsLoading = TeamMembers.Any(m => m.IsLoading);
                    RaiseMemberProperties();
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    // 所有成员数据加载完毕时显示覆盖层提示框
                    if (TeamMembers.Count >= 2 && TeamMembers.All(m => !m.IsLoading) && !_overlayShownForThisRound && !_overlayDismissedThisRound)
                    {
                        _overlayShownForThisRound = true;
                        var overlayMembers = TeamMembers.Select(m => new TeamOverlayMemberItem
                        {
                            UserName = m.UserName,
                            AvatarUrl = m.AvatarUrl,
                            RankName = m.RankName,
                            RankIcon = m.RankIcon,
                            PageRankName = m.PageRankName,
                            PageStarCount = m.PageStarCount,
                            PageHasStars = m.PageHasStars,
                            RankTierScore = m.RankTierScore
                        }).ToList();
                        _teamOverlayService.Show(overlayMembers);
                        _teamOverlayService.RefreshAction = RefreshFromOverlay;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TeamInfo] Final cleanup error: " + ex.Message);
                try
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        member.IsLoading = false;
                    });
                }
                catch { }
            }
        }

        private void UpdateOverlayMembers()
        {
            if (_overlayDismissedThisRound) return;
            if (TeamMembers.Count < 2) return;
            if (!_overlayShownForThisRound)
            {
                _overlayShownForThisRound = true;
                _teamOverlayService.RefreshAction = RefreshFromOverlay;
            }

            var overlayMembers = TeamMembers.Select(m => new TeamOverlayMemberItem
            {
                UserName = m.UserName,
                AvatarUrl = m.AvatarUrl,
                RankName = m.RankName,
                RankIcon = m.RankIcon,
                PageRankName = m.PageRankName,
                PageStarCount = m.PageStarCount,
                PageHasStars = m.PageHasStars,
                RankTierScore = m.RankTierScore,
                IsLoading = m.IsLoading
            }).ToList();
            _teamOverlayService.Show(overlayMembers);
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
                // LoadMemberDataAsync 自身已是异步方法（ConfigureAwait(false) + UIDispatcher.InvokeAsync），
                // 无需再用 Task.Run 多嵌一层 ThreadPool；直接收集 Task 即可。
                tasks.Add(LoadMemberDataAsync(member, ct));
            }
            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
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
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); }
            catch (Exception ex) { Debug.WriteLine($"[{nameof(TeamInfoPageViewModel)}] CancelAndDispose Cancel failed: {ex.Message}"); }
            cts.Dispose();
            cts = null;
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            // 基类签名为 void，非事件 handler，禁止误标 async；页面初始化仅触发 fire-and-forget 异步任务即可。
            base.OnNavigatedToExecute(navigationContext);

            // 进入页面时订阅 game status 事件，捕获英雄选择阶段
            _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;
            _teamOverlayService.Dismissed += OnOverlayDismissed;

            _ = LoadSeasonsAsync();

            if (_gameStatusMonitor.CurrentStatus == GameStatus.HeroSelection)
            {
                IsHeroSelectionPhase = true;
                _overlayShownForThisRound = false;
                StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                StartOcrLoop();
            }
            else if (_gameStatusMonitor.CurrentStatus == GameStatus.InGame && TeamMembers.Count > 0)
            {
                // 游戏中且已有识别的队伍数据：保留数据，不清理
                // 可能是从英雄选择阶段自然过渡到游戏中，或是用户手动导航到此页面
                IsHeroSelectionPhase = false;
                StatusText = string.Empty;
                _teamOverlayService.Hide();
            }
            else
            {
                // 非英雄选择状态时清除旧队友数据，避免复杂UI（DropShadowEffect + 索引绑定）立即渲染导致卡死
                if (TeamMembers.Count > 0)
                {
                    _hasEverHadData = false;
                    TeamMembers.Clear();
                    DiffLeft.Clear();
                    DiffRight.Clear();
                    RaiseMemberProperties();
                }

                IsHeroSelectionPhase = false;
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
                    await _uiDispatcher.InvokeAsync(() =>
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
            // 离开页面时取消订阅，避免 VM 泄漏。
            // 注意：OnNavigatedToExecute 中订阅了两个事件，必须**一一对称解绑**：
            //   - _gameStatusMonitor.GameStatusRecognized
            //   - _teamOverlayService.Dismissed  ← 之前漏解，每次进出页面累加一个 handler
            //     N 次导航后 OnOverlayDismissed 会被触发 N 次，且 VM 永不被 GC 释放。
            // 同理 _teamOverlayService.RefreshAction 也持有 VM 的方法回调（RefreshFromOverlay），
            // overlay service 是单例，必须在此对称清空，防止 VM 通过委托被外部单例长期持引用。
            _gameStatusMonitor.GameStatusRecognized -= OnGameStatusRecognized;
            _teamOverlayService.Dismissed -= OnOverlayDismissed;
            _teamOverlayService.RefreshAction = null;

            StopOcrLoop();
            CancelAndDispose(ref _refreshMembersCts);
            CancelAndDispose(ref _refreshOcrCts);
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
        public string DiffTooltip { get; set; } = string.Empty;
    }

    public class TeamMemberInfo : ViewModelBase
    {
        private readonly IClipboardService? _clipboard;
        private readonly ILocalizedTextProvider? _localizedText;

        /// <summary>
        /// 默认 ctor 仅供 d:DesignInstance 等设计时反射用；运行时所有 TeamMemberInfo 必须走 ctor(IClipboardService, ILocalizedTextProvider)。
        /// </summary>
        public TeamMemberInfo() { }

        public TeamMemberInfo(IClipboardService clipboard, ILocalizedTextProvider localizedText)
        {
            _clipboard = clipboard;
            _localizedText = localizedText;
        }

        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set
            {
                if (_userName == value) return;
                _userName = value;
                RaisePropertyChanged(nameof(UserName));
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

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                RaisePropertyChanged(nameof(StatusText));
                RaisePropertyChanged(nameof(HasStatusError));
            }
        }

        public bool HasStatusError => !string.IsNullOrEmpty(_statusText);

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
            }
        }

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

        private double _soloRankScore;
        public double SoloRankScore
        {
            get => _soloRankScore;
            set
            {
                if (_soloRankScore == value) return;
                _soloRankScore = value;
                RaisePropertyChanged(nameof(SoloRankScore));
            }
        }

        private double _duoRankScore;
        public double DuoRankScore
        {
            get => _duoRankScore;
            set
            {
                if (_duoRankScore == value) return;
                _duoRankScore = value;
                RaisePropertyChanged(nameof(DuoRankScore));
            }
        }

        private double _trioRankScore;
        public double TrioRankScore
        {
            get => _trioRankScore;
            set
            {
                if (_trioRankScore == value) return;
                _trioRankScore = value;
                RaisePropertyChanged(nameof(TrioRankScore));
            }
        }

        // Stats dictionary: key -> display value
        public System.Collections.Generic.Dictionary<string, string> Stats { get; } = new();

        private string _killCount = string.Empty;
        public string KillCount
        {
            get => _killCount;
            set
            {
                if (_killCount == value) return;
                _killCount = value;
                RaisePropertyChanged(nameof(KillCount));
            }
        }

        private string _top5Rate = string.Empty;
        public string Top5Rate
        {
            get => _top5Rate;
            set
            {
                if (_top5Rate == value) return;
                _top5Rate = value;
                RaisePropertyChanged(nameof(Top5Rate));
            }
        }

        private string _damagePlayer = string.Empty;
        public string DamagePlayer
        {
            get => _damagePlayer;
            set
            {
                if (_damagePlayer == value) return;
                _damagePlayer = value;
                RaisePropertyChanged(nameof(DamagePlayer));
            }
        }

        private string _surviveTime = string.Empty;
        public string SurviveTime
        {
            get => _surviveTime;
            set
            {
                if (_surviveTime == value) return;
                _surviveTime = value;
                RaisePropertyChanged(nameof(SurviveTime));
            }
        }

        public System.Action<TeamMemberInfo>? RefreshAction { get; set; }

        private DelegateCommand? _searchMemberCommand;
        public DelegateCommand SearchMemberCommand =>
            _searchMemberCommand ??= new DelegateCommand(() =>
            {
                RefreshAction?.Invoke(this);
            });

        // 通过 ILocalizedTextProvider 访问字符串本地化资源，避免 VM 直接依赖 System.Windows.Application。
        // 设计时默认 ctor 可能没有 provider，此时回退到中文 fallback。
        private string CopySuccessText() =>
            _localizedText?.Get("Stats.CopySuccess", "复制成功") ?? "复制成功";

        private DelegateCommand? _copyUserNameCommand;
        public DelegateCommand CopyUserNameCommand =>
            _copyUserNameCommand ??= new DelegateCommand(() =>
            {
                _clipboard?.TrySetText(UserName);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(CopySuccessText()));
            });

        private DelegateCommand? _copyUIDCommand;
        public DelegateCommand CopyUIDCommand =>
            _copyUIDCommand ??= new DelegateCommand(() =>
            {
                _clipboard?.TrySetText(UID);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(CopySuccessText()));
            });

        public System.Action<string>? NavigateToStatsAction { get; set; }

        private DelegateCommand? _navigateToStatsCommand;
        public DelegateCommand NavigateToStatsCommand =>
            _navigateToStatsCommand ??= new DelegateCommand(() =>
            {
                NavigateToStatsAction?.Invoke(UserName);
            });

    }
}
