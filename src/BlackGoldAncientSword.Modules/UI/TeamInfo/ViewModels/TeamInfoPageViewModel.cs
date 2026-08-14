using System.Collections.ObjectModel;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using System.Linq;
using System.Runtime;
using BlackGoldAncientSword.Framework.Services.Abstractions;
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
        private readonly ICcMiniTeammateMonitor _teammateMonitor;
        private readonly TeamMemberLoader _memberLoader;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly ITipMessageService _tipMessage;
        private bool _isMonitoring;
        private readonly object _monitorLock = new();
        private bool _isHeroSelectionPhase;
        private CancellationTokenSource? _refreshMembersCts;
        private CancellationTokenSource? _refreshOcrCts;
        private CancellationTokenSource? _loadSeasonsCts;
        private readonly SearchDebounceGate _refreshOcrDebounce = new();
        // 筛选器（赛季/排数/大类）变更合并防抖：连续改多个条件只在最后一次之后触发一次整队重查，
        // 避免每个 setter 各自发一批相同参数的 HTTP（实测重复请求根因）。
        private const int FilterRefreshDebounceMs = 200;
        private readonly TrailingDebouncer _filterRefreshDebouncer;
        // 已成功加载赛季后不再重复请求；后续切换页面也复用既有数据。
        private bool _seasonsLoaded;
        private bool _hasEverHadData;
        private bool _overlayShownForThisRound;
        private bool _overlayDismissedThisRound;
        /// <summary>
        /// 标记是否已成功从语音日志识别到完整队伍名单（含数据加载）。
        /// 用于保护性判断：已有有效数据时不再重复监听，但用户仍可通过刷新按钮手动触发重查。
        /// 在游戏状态变为 Unknown 时重置。
        /// </summary>
        private bool _teamDataLoadedSuccessfully;

        // 通过 ILocalizedTextProvider 访问字符串本地化资源，避免 VM 直接依赖 System.Windows.Application。
        private string L(string key, string fallback) => _localizedText.Get(key, fallback);

        public TeamInfoPageViewModel(
            IGameStatusMonitor gameStatusMonitor,
            IPlayerPrefsService playerPrefsService,
            ITeamOverlayService teamOverlayService,
            IMainContentNavigationService navigation,
            IUIDispatcher uiDispatcher,
            IClipboardService clipboard,
            ICcMiniTeammateMonitor teammateMonitor,
            TeamMemberLoader memberLoader,
            ILocalizedTextProvider localizedText,
            ITipMessageService tipMessage)
        {
            _gameStatusMonitor = gameStatusMonitor;
            _teamOverlayService = teamOverlayService;
            _playerPrefsService = playerPrefsService;
            _navigation = navigation;
            _uiDispatcher = uiDispatcher;
            _clipboard = clipboard;
            _teammateMonitor = teammateMonitor;
            _memberLoader = memberLoader;
            _localizedText = localizedText;
            _tipMessage = tipMessage;

            TeamMembers = new ObservableCollection<TeamMemberInfo>();
            Seasons = new ObservableCollection<UnifiedSeason>();
            MergedStatRows = new ObservableCollection<MergedStatRow>();
            _selectedTeamSize = TeamSize.Trio;
            _selectedCategory = GameModeCategory.Rank;
            // _statusText 不能用字段初始化器调 L()，因为 _localizedText 此时还未注入。
            _statusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");

            // 构造函数中永久订阅（不依赖页面导航），确保进入英雄选择后立即启动日志监听。
            _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;
            _teamOverlayService.Dismissed += OnOverlayDismissed;
            _teammateMonitor.TeammatesReady += OnTeammatesReady;

            _filterRefreshDebouncer = new TrailingDebouncer(FilterRefreshDebounceMs, RunFilterRefreshAsync);
        }

        // === Filters ===
        public ObservableCollection<UnifiedSeason> Seasons { get; }

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

        private TeamSize _selectedTeamSize;
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

        private GameModeCategory _selectedCategory;
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

        private DelegateCommand? _refreshOcrCommand;
        public DelegateCommand RefreshOcrCommand =>
            _refreshOcrCommand ??= new DelegateCommand(async () =>
            {
                if (!_refreshOcrDebounce.TryEnter())
                {
                    _tipMessage.ShowError(L("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }
                await RefreshTeamMemberDataAsync();
            });


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

        // 成员卡片"内容行"（战绩表格）可见性：仅在成员存在且没有查询失败时显示。
        // 查询失败时改由错误覆盖层承担整卡显示，避免旧数据行泄漏出来。
        public bool Member0Ready => HasMember0 && !TeamMembers[0].HasStatusError;
        public bool Member1Ready => HasMember1 && !TeamMembers[1].HasStatusError;
        public bool Member2Ready => HasMember2 && !TeamMembers[2].HasStatusError;

        // === Merged stat rows（含 diff）===
        public ObservableCollection<MergedStatRow> MergedStatRows { get; }

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
            BlackGoldAncientSword.Framework.Core.Infrastructure.DiagLog.Write(
                "VM", $"OnGameStatusRecognized status={args.Status}, dataLoaded={_teamDataLoadedSuccessfully}");

            // 对局结束/状态未知时立即停止日志监听，无需等待 UI 线程调度。
            // InGame 不在此列：进入对局后队友仍可能退出/换人，需保持监听以更新卡片。
            // GameLogMonitor 在 ThreadPool 上触发事件，HandleGameStatusOnUiThread 通过
            // _uiDispatcher.InvokeAsync marshal 到 UI 线程，从事件触发到实际执行 StopMonitor
            // 之间存在延迟窗口。StopMonitor 内部持有 _monitorLock，线程安全，可从任意线程调用。
            if (args.Status is GameStatus.BattleEnded or GameStatus.Unknown)
            {
                StopMonitor();
            }

            // 注意：不能在这里因 _teamDataLoadedSuccessfully=True 就早退。
            // BattleStateMachine 只在 !alreadyInBattle 时才 emit Joined（同一局的掉线重连不会重复
            // emit），所以每次收到 HeroSelection 事件都代表"新一局英雄选择开始"，必须重置
            // _teamDataLoadedSuccessfully 并重启监听。
            // 若上一局异常退出（玩家杀进程 / crash，无 TeamBattle Destroy 等 Ended marker），
            // _teamDataLoadedSuccessfully 会残留 True；此处若早退就永远跳过 HandleGameStatusOnUiThread
            // 里的重置，导致新一局英雄选择阶段日志监听永不启动（实测复现的 bug）。
            // 统一交给下方 HandleGameStatusOnUiThread 的 HeroSelection 分支处理（重置 + StartMonitor）。

            // HandleGameStatusOnUiThread 中 BattleEnded/Unknown 分支仍会调用
            // StopMonitor()，但 _isMonitoring 已为 false 时会通过 lock 内早期 return 快速退出。

            // GameStatusRecognized 可能从 ThreadPool 触发（GameLogMonitor 的 FileSystemWatcher.OnLogChanged
            // 走 Task.Run → 同步 raise BattleStarted/Ended/Joined → MainWindowViewModel/HomePageViewModel
            // 同步调 _gameStatusMonitor.NotifyStatus → 同步 raise GameStatusRecognized）。
            // 下方 HandleGameStatusOnUiThread 会修改 ObservableCollection（TeamMembers.Clear/MergedStatRows.Clear），
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
                    _overlayDismissedThisRound = false;
                    _teamDataLoadedSuccessfully = false;
                    StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                    // 新一局开始：立即清掉上一局残留的卡片数据，避免在 Monitor 重新识别出当前队友前
                    // 短暂展示上一局队友（异常退出无 BattleEnded 时 TeamMembers 会残留）。
                    if (TeamMembers.Count > 0)
                    {
                        _hasEverHadData = false;
                        TeamMembers.Clear();
                        MergedStatRows.Clear();
                        RaiseMemberProperties();
                    }
                    // 清空 Monitor 上一局残留的 UID 与触发快照；记录本局英雄选择开始时间，
                    // CCM 只接受该时间之后的 set-uid-vol（m*.log 跨局复用时丢弃上一局旧记录），
                    // 并保留读取位置，Start 只增量读新写入的 UID（见 CcMiniTeammateMonitor.Reset）。
                    _teammateMonitor.Reset(DateTime.Now);
                    StartMonitor();
                    break;
                case GameStatus.InGame:
                    IsHeroSelectionPhase = false;
                    _teamOverlayService.Hide();
                    // 不 StopMonitor：进入对局后队友仍可能退出/换人（set-uid-vol 增量写入新 UID），
                    // 需保持监听以更新卡片，直到本局结束（BattleEnded/Unknown）才停。
                    ClearImageMemoryCaches();
                    break;
                case GameStatus.BattleEnded:
                    IsHeroSelectionPhase = false;
                    _teamDataLoadedSuccessfully = false;
                    _teamOverlayService.Hide();
                     StopMonitor();
                    ClearImageMemoryCaches();
                    StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
                     if (TeamMembers.Count > 0)
                     {
                         _hasEverHadData = false;
                         TeamMembers.Clear();
                         MergedStatRows.Clear();
                         RaiseMemberProperties();
                     }
                    break;
                case GameStatus.Unknown:
                    IsHeroSelectionPhase = false;
                    _teamOverlayService.Hide();
                     StopMonitor();
                    StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
                     if (TeamMembers.Count > 0)
                    ClearImageMemoryCaches();
                     {
                         _hasEverHadData = false;
                         _teamDataLoadedSuccessfully = false;
                         TeamMembers.Clear();
                         MergedStatRows.Clear();
                         RaiseMemberProperties();
                     }
                    break;
            }
        }

        private void StartMonitor()
        {
            BlackGoldAncientSword.Framework.Core.Infrastructure.DiagLog.Write(
                "VM", $"StartMonitor 入口, dataLoaded={_teamDataLoadedSuccessfully}, isRunning={_isMonitoring}");

            // 如果上一轮已成功识别（数据已加载），不再重复监听。
            // 用户仍可通过右上角的刷新按钮手动触发单次重查（RefreshTeamMemberDataCommand）。
            if (_teamDataLoadedSuccessfully)
            {
                Debug.WriteLine($"[{nameof(TeamInfoPageViewModel)}] Skip monitor: valid data already loaded.");
                return;
            }

            lock (_monitorLock)
            {
                if (_isMonitoring) return;
                _isMonitoring = true;
            }
            _teammateMonitor.Start();
        }

        /// <summary>
        /// 语音日志识别到/更新队友名单时触发（Monitor 的 ThreadPool 线程）。
        /// 首次识别与对局中队友退出/换人（UID 集合变化）都会走到这里，更新卡片数据。
        /// marshal 回 UI 线程后写入队伍页。
        /// </summary>
        private void OnTeammatesReady(object? sender, CcMiniTeammatesEventArgs args)
        {
            DiagLog.Write("VM", $"OnTeammatesReady: {args.TeammateUids.Count} 名队友");
            if (!_uiDispatcher.CheckAccess())
            {
                _ = _uiDispatcher.InvokeAsync(() => HandleTeammatesReady(args));
                return;
            }
            HandleTeammatesReady(args);
        }

        private void HandleTeammatesReady(CcMiniTeammatesEventArgs args)
        {
            if (args.TeammateUids.Count == 0) return;
            // 不做早退：队友在英雄选择阶段可能退出/换人（UID 集合变化），每次变化都要更新卡片。
            _teamDataLoadedSuccessfully = true;
            _ = SafeUpdateTeamMembers(args.TeammateUids);
        }

        private void OnOverlayDismissed()
        {
            _overlayDismissedThisRound = true;
        }

        private void StopMonitor()
        {
            lock (_monitorLock)
            {
                if (!_isMonitoring) return;
                _isMonitoring = false;
            }
            _teammateMonitor.Stop();
        }

        /// <summary>
        /// 手动刷新：用 Monitor 最近一次识别到的 UID（含已加载/失败重试）重建队伍数据。
        /// </summary>
        private async Task RefreshTeamMemberDataAsync()
        {
            // 取消之前的刷新操作
            CancelAndDispose(ref _refreshOcrCts);
            _refreshOcrCts = new CancellationTokenSource();
            var ct = _refreshOcrCts.Token;

            _overlayShownForThisRound = false;
            try
            {
                StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");

                var uids = _teammateMonitor.TeammateUids;
                if (uids.Count == 0) return;

                await ReloadLocalUserAsync();
                await UpdateTeamMembersAsync(uids);
                _teamDataLoadedSuccessfully = true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[TeamInfo] Refresh team data cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "TeamInfo", "Refresh team data error");
            }
        }


        /// <summary>
        /// 覆盖层弹窗请求刷新时的回调。重置覆盖层标志后走完整的刷新流程，
        /// 刷新完成后覆盖层会自动重新显示。
        /// <para>
        /// **async void 安全契约**：本方法被 ITeamOverlayService.RefreshAction (Action 类型) 持有调用，
        /// 必须保持 async void 签名。<see cref="RefreshTeamMemberDataAsync"/> 内部完整 try/catch 兜底所有 await 异常，
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
                await RefreshTeamMemberDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.{nameof(RefreshFromOverlay)}");
            }
        }

        /// <summary>
        /// fire-and-forget 包装：捕获 <see cref="UpdateTeamMembersAsync"/> 异常以避免
        /// UnobservedTaskException（OperationCanceledException 视为正常取消）。
        /// </summary>
        private async Task SafeUpdateTeamMembers(IReadOnlyList<string> uids)
        {
            DiagLog.Write("VM", $"SafeUpdateTeamMembers 入口: {uids.Count}名");
            try
            {
                await UpdateTeamMembersAsync(uids);
            }
            catch (OperationCanceledException)
            {
                DiagLog.Write("VM", "SafeUpdateTeamMembers 被取消(OperationCanceledException)");
            }
            catch (Exception ex)
            {
                DiagLog.Write("VM", $"SafeUpdateTeamMembers 异常: {ex.GetType().Name}: {ex.Message}");
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.{nameof(SafeUpdateTeamMembers)}");
            }
        }

        /// <summary>
        /// 校正本地用户名前主动重读 player_prefs，确保 <see cref="IPlayerPrefsService.Current"/>
        /// 与战绩页用的是同一份最新本地登录账号。
        /// </summary>
        /// <remarks>
        /// 战绩页每次进入都会 reload，而队友卡片原先只用构造时的陈旧快照——用户在游戏内切了
        /// Steam↔网易客户端后，两页定位到的"本地用户"就会不一致。reload 一次即可对齐。
        /// reload 失败不阻断识别，沿用旧快照兜底。
        /// </remarks>
        private async Task ReloadLocalUserAsync()
        {
            try
            {
                await _playerPrefsService.LoadAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.{nameof(ReloadLocalUserAsync)}", "reload prefs failed");
            }
        }

        // 注意：本方法刻意不接收监控循环的 CancellationToken。
        // 语音日志已识别到有效 UID（_teamDataLoadedSuccessfully 已置 true），把结果写入 UI 这一步
        // 必须无条件完成——绝不能因 token 在 await(ReloadLocalUser/LoadSeasons) 让出线程
        // 期间被取消，就把已识别的成员整批丢弃（实测 bug：识别到 3 人却因 ct 取消
        // 抛 OperationCanceledException，TeamMembers 一个都没加，页面空白）。
        // 成员 HTTP 加载有独立的 _refreshMembersCts（loadCt），其取消语义与此无关。
        private async Task UpdateTeamMembersAsync(IReadOnlyList<string> uids)
        {
            if (uids.Count == 0) return;

            await ReloadLocalUserAsync();
            var localUid = _playerPrefsService.Current.PlayerId;
            var localName = _playerPrefsService.Current.OriginalPlayerName;
            DiagLog.Write("VM",
                $"UpdateTeamMembers 入口: uids=[{string.Join(" | ", uids)}], localUid={localUid}, localName='{localName}', 当前TeamMembers={TeamMembers.Count}");

            // 本地用户格判定改为按 UID 精确匹配：语音日志给出的 UID 中本地用户必有一个等于
            // PlayerId（来自 player_prefs 的 player_id）。队友格全部按 UID 查询。
            // 若本地 UID 不在名单中（极端：本地 UID 读取失败），则把首格视为本地用户兜底。

            // 队伍规模由队友 UID 数量决定（语音日志 set-uid-vol 只给队友设音量）：
            // 1 名队友 = 双排，2 名队友 = 三排。先直接写字段（不触发筛选器防抖重查），
            // 再手动 RaisePropertyChanged 让排数 radiobutton 同步切换，最后按它查询 stats。
            // 不能走 SelectedTeamSize setter：setter 会触发 _filterRefreshDebouncer，
            // 200ms 后 CancelAndDispose 掉本方法刚建的发查询 token，整队重查一遍（重复 HTTP 根因）。
            if (uids.Count == 1)
                _selectedTeamSize = TeamSize.Duo;
            else if (uids.Count >= 2)
                _selectedTeamSize = TeamSize.Trio;
            RaisePropertyChanged(nameof(SelectedTeamSize));

            // Mark that we have loaded data at least once
            _hasEverHadData = true;

            // 给成员数据加载用独立 token，不复用监控生命周期 token。
            // member-scoped 加载只应在用户主动整队刷新（RefreshTeamMemberData）或离开页面时取消。
            if (_refreshMembersCts == null || _refreshMembersCts.IsCancellationRequested)
            {
                CancelAndDispose(ref _refreshMembersCts);
                _refreshMembersCts = new CancellationTokenSource();
            }
            var loadCt = _refreshMembersCts.Token;

            // 按 UID 去重名单；本地用户格也占一格（作为查询模板 / diff 基准）。
            var recognizedUids = uids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // 若本地 UID 不在语音日志给出的队友 UID 中，补上本地用户格（中间卡）。
            var localPresent = !string.IsNullOrEmpty(localUid)
                               && recognizedUids.Contains(localUid, StringComparer.OrdinalIgnoreCase);
            if (!localPresent)
            {
                // 本地 UID 读取失败或本地用户在 set-uid-vol 中未出现：仍保留本地卡（本地用户必在队伍）。
                recognizedUids.Add(localUid);
            }

            // 移除已不在本局名单中的旧成员。
            var recognizedSet = recognizedUids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = TeamMembers.Where(m => !recognizedSet.Contains(m.UID)).ToList();
            foreach (var r in removed)
                TeamMembers.Remove(r);

            foreach (var uid in recognizedUids)
            {
                // 已存在且已成功加载（有 UID + stats + 无错误）：跳过，不重发 HTTP。
                var existing = TeamMembers.FirstOrDefault(m =>
                    string.Equals(m.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    var alreadyLoaded = !string.IsNullOrEmpty(existing.UID)
                                        && existing.Stats.Count > 0
                                        && !existing.HasStatusError;
                    if (alreadyLoaded) continue;
                    // 上次失败或未加载完：重置状态并重新请求。
                    existing.IsLoading = true;
                    existing.StatusText = string.Empty;
                    _ = LoadWithIndependentToken(existing, loadCt);
                    continue;
                }

                var isLocal = !string.IsNullOrEmpty(localUid)
                              && string.Equals(uid, localUid, StringComparison.OrdinalIgnoreCase);
                var member = new TeamMemberInfo(_clipboard, _localizedText, _tipMessage)
                {
                    UID = uid,
                    UserName = isLocal && !string.IsNullOrWhiteSpace(localName) ? localName : uid,
                    IsLocalUser = isLocal,
                    IsLoading = true,
                    RefreshAction = RefreshSingleMember,
                    NavigateToStatsAction = NavigateToMemberStats
                };
                TeamMembers.Add(member);
                _ = LoadWithIndependentToken(member, loadCt);
            }

            ReorderMembersForLocalUser();
            UpdateDiffs();
            IsLoading = TeamMembers.Any(m => m.IsLoading);
            RaiseMemberProperties();
            DiagLog.Write("VM",
                $"UpdateTeamMembers 完成: TeamMembers={TeamMembers.Count}, 开始HTTP加载");

            // 队友变化（有人退出/重进）时立即刷右下角弹窗，确保队员列表实时更新。
            UpdateOverlayMembers();
        }

        private void NavigateToMemberStats(TeamMemberInfo member)
        {
            // 本地用户卡片：不带 TargetPlayerName 导航到战绩页，战绩页会 reload 本地用户
            // 并展示本地 UID —— 等价于战绩页「回到我」的效果，而非用队友名去查自己。
            var parameters = new NavigationParameters();
            if (!member.IsLocalUser)
                parameters.Add(NavigationParameterKeys.TargetPlayerName, member.UserName);
            _navigation.NavigateTo(PageNames.StatsPage, parameters);
        }

        private void ReorderMembersForLocalUser()
        {
            // 2 人或 3 人队里必然有一格是本地用户。定位规则：本地用户 UID == player_prefs 的
            // PlayerId（本地卡在 UpdateTeamMembersAsync 中已置 IsLocalUser）。无条件强制，
            // 移到中间卡片 index 1，其余格即队友。
            if (TeamMembers.Count < 2) return;

            var localIdx = ResolveLocalUserIndex();
            if (localIdx < 0) return;

            // 中间卡片固定为 index 1（3 人队 = 正中，2 人队 = 带蓝色高亮边框的 Member1）。
            if (localIdx != 1)
                TeamMembers.Move(localIdx, 1);

            MarkLocalUserSlot();
        }

        /// <summary>
        /// 标记中间格（index 1）为本地用户：置 <see cref="TeamMemberInfo.IsLocalUser"/>，
        /// 并把该格搜索框展示文本回写为本地登录名 OriginalPlayerName（本地名本地可知）。
        /// 其余格清除标志。
        /// </summary>
        private void MarkLocalUserSlot()
        {
            var localName = _playerPrefsService.Current.OriginalPlayerName;
            for (int i = 0; i < TeamMembers.Count; i++)
            {
                var isLocal = i == 1;
                TeamMembers[i].IsLocalUser = isLocal;
                if (isLocal && !string.IsNullOrWhiteSpace(localName))
                    TeamMembers[i].UserName = localName;
            }
        }

        /// <summary>
        /// 定位本地用户在 <see cref="TeamMembers"/> 中的下标，供重排与 diff 计算共用，保证口径一致。
        /// <para>
        /// 优先用创建时设置的 <see cref="TeamMemberInfo.IsLocalUser"/> 标志（UpdateTeamMembersAsync
        /// 在生成本地卡时已精确判定），因为它不依赖 UID 字符串格式——后端返回的 RoleIdSimple 是纯数字
        /// （如 15949400120163），与本地 PlayerId（l77c000015949400120163）带前缀不同，字符串匹配必失败。
        /// UID 匹配仅作兜底（极端：标志未设置）。
        /// </para>
        /// </summary>
        private int ResolveLocalUserIndex()
        {
            if (TeamMembers.Count == 0) return -1;

            // 1) 首选：IsLocalUser 标志（创建时已按 PlayerId 精确判定）。
            for (int i = 0; i < TeamMembers.Count; i++)
            {
                if (TeamMembers[i].IsLocalUser) return i;
            }

            // 2) 兜底：UID 精确匹配 PlayerId（仅当标志未设置时生效）。
            var localUid = _playerPrefsService.Current.PlayerId;
            if (!string.IsNullOrEmpty(localUid))
            {
                for (int i = 0; i < TeamMembers.Count; i++)
                {
                    if (string.Equals(TeamMembers[i].UID, localUid, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return 0;
        }

        private int LocalUserIndex
        {
            get => ResolveLocalUserIndex();
        }

        // 段位分行的合成 key：后端 metrics 不含段位分，作为固定尾行单独追加。
        private const string RankRowKey = "__rank__";

        /// <summary>
        /// 重建 MergedStatRows：数据行以本地用户（中间栏）后端返回的 metrics 为准动态生成，
        /// 与战绩页「数据详情」完全一致（后端给什么就展示什么，顺序也一致），末尾追加"段位分"行。
        /// 三栏按 metric.code 对齐取值，缺失成员填 "-"；diff 列按 code 计算。
        /// 一个集合绑定一个 ItemsControl，每行模板是 5 列 Grid，天然水平对齐。
        /// </summary>
        private void UpdateDiffs()
        {
            MergedStatRows.Clear();

            var m0 = TeamMembers.Count > 0 ? TeamMembers[0] : null;
            var m1 = TeamMembers.Count > 1 ? TeamMembers[1] : null;
            var m2 = TeamMembers.Count > 2 ? TeamMembers[2] : null;

            var localIdx = LocalUserIndex;
            // 未定位到本地用户（-1）时 diff 两侧退化为普通左右相邻比较，模板取任一有数据的成员。
            TeamMemberInfo? diffLeftA = null, diffLeftB = null;
            TeamMemberInfo? diffRightA = null, diffRightB = null;
            if (m0 != null && m1 != null)
            {
                if (localIdx == 0) { diffLeftA = m0; diffLeftB = m1; }
                else if (localIdx == 1) { diffLeftA = m1; diffLeftB = m0; }
                else { diffLeftA = m0; diffLeftB = m1; }
            }
            if (m1 != null && m2 != null)
            {
                if (localIdx == 1) { diffRightA = m1; diffRightB = m2; }
                else if (localIdx == 2) { diffRightA = m2; diffRightB = m1; }
                else { diffRightA = m1; diffRightB = m2; }
            }

            // 行模板优先取本地用户返回的 metrics；未定位到本地用户或其查询失败/未加载时，
            // 回退到任一有 metrics 的成员，避免整表空白。
            var localMember = localIdx >= 0 && localIdx < TeamMembers.Count ? TeamMembers[localIdx] : null;
            var template = (localMember != null && localMember.Metrics.Count > 0)
                ? localMember.Metrics
                : TeamMembers.FirstOrDefault(x => x.Metrics.Count > 0)?.Metrics
                  ?? new List<Services.PlayerStatMetric>();

            foreach (var metric in template)
            {
                var def = (metric.Key, metric.Label, metric.IsPercent);
                var row = new MergedStatRow
                {
                    Label = metric.Label,
                    Val0 = GetStatVal(m0, metric.Key),
                    Val1 = GetStatVal(m1, metric.Key),
                    Val2 = GetStatVal(m2, metric.Key),
                };

                if (diffLeftA != null && diffLeftB != null)
                    FillDiff(row, isLeft: true, diffLeftA, diffLeftB, def);
                if (diffRightA != null && diffRightB != null)
                    FillDiff(row, isLeft: false, diffRightA, diffRightB, def);

                MergedStatRows.Add(row);
            }

            // 段位分固定尾行：后端 metrics 不含，用 __rank__ 合成 key 取各成员 RankScore。
            var rankDef = (RankRowKey, L("TeamInfo.RankScore", "段位分"), false);
            var rankRow = new MergedStatRow
            {
                Label = rankDef.Item2,
                Val0 = GetStatVal(m0, RankRowKey),
                Val1 = GetStatVal(m1, RankRowKey),
                Val2 = GetStatVal(m2, RankRowKey),
            };
            if (diffLeftA != null && diffLeftB != null)
                FillDiff(rankRow, isLeft: true, diffLeftA, diffLeftB, rankDef);
            if (diffRightA != null && diffRightB != null)
                FillDiff(rankRow, isLeft: false, diffRightA, diffRightB, rankDef);
            MergedStatRows.Add(rankRow);

            RaisePropertyChanged(nameof(HasDiffLeft));
            RaisePropertyChanged(nameof(HasDiffRight));
        }

        private static string GetStatVal(TeamMemberInfo? m, string key)
        {
            if (m == null) return "-";
            if (key == RankRowKey) return m.RankScore > 0 ? m.RankScore.ToString("F0") : "-";
            return m.Stats.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : "-";
        }

        private static void FillDiff(MergedStatRow row, bool isLeft,
            TeamMemberInfo a, TeamMemberInfo b,
            (string Key, string Label, bool IsPercent) def)
        {
            double av, bv;
            if (def.Key == RankRowKey)
            {
                av = a.RankScore; bv = b.RankScore;
            }
            else
            {
                av = a.Stats.TryGetValue(def.Key, out var al) ? TryParseDouble(al) : 0;
                bv = b.Stats.TryGetValue(def.Key, out var bl) ? TryParseDouble(bl) : 0;
            }
            var diff = av - bv;
            const string fmt = "0.##";
            string text, color;
            if (Math.Abs(diff) < 0.001) { text = "0"; color = "#999999"; }
            else if (diff > 0) { text = def.IsPercent ? $"+{diff:F1}%" : $"+{diff.ToString(fmt)}"; color = "#22AA22"; }
            else { text = def.IsPercent ? $"{diff:F1}%" : $"{diff.ToString(fmt)}"; color = "#DD3333"; }

            if (isLeft) { row.DiffLeftText = text; row.DiffLeftColor = color; }
            else { row.DiffRightText = text; row.DiffRightColor = color; }
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

        /// <summary>
        /// 为每个成员创建独立的 linked CancellationTokenSource，使各成员 HTTP 请求完全独立：
        /// 父 token 取消时所有成员同步取消（导航离开、切换筛选器），
        /// 但单个成员查询失败不会影响其他成员的 token。
        /// </summary>
        private async Task LoadWithIndependentToken(TeamMemberInfo member, CancellationToken parentToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            await LoadMemberDataAsync(member, cts.Token);
        }

        private async Task LoadMemberDataAsync(TeamMemberInfo member, CancellationToken ct)
        {
            var userName = member.UserName;
            if (string.IsNullOrWhiteSpace(userName)) return;

            try
            {
                // 已有解析好的 ctx（首次 search+player 成功过）：筛选器变更重查时复用，
                // 跳过 search/player（identity 与资料不变），只重查 season(stats)。
                var ctx = member.SourceContext;
                if (ctx != null)
                {
                    var stats = await _memberLoader.LoadStatsOnlyAsync(
                        ctx,
                        _selectedSeason?.Code,
                        _selectedCategory,
                        _selectedTeamSize,
                        ct).ConfigureAwait(false);
                    await ApplyMemberStatsAsync(member, stats);
                    DiagLog.Write("TeamVM", $"复用 ctx 只重查 season: uid={member.UID}");
                }
                else
                {
                    // 所有成员按 UID 查询：优先用 member.UID 作为 SearchRecord 的 keyword（UID 精确命中），
                    // 用户名（UserName）仅作兜底回退。本地用户格与队友格无差别——都走 UID 优先。
                    var uidOverride = string.IsNullOrWhiteSpace(member.UID) ? null : member.UID;

                    // Step 1-4: 调 Loader 在后台线程拉数据；返回 DTO 后回 UI 线程逐批写回属性。
                    var loaded = await _memberLoader.LoadAsync(
                        userName,
                        _selectedSeason?.Code,
                        _selectedCategory,
                        _selectedTeamSize,
                        ct,
                        uidOverride).ConfigureAwait(false);

                    if (loaded.Failed)
                    {
                        // 优先展示后端 msg 原文（如"未找到玩家"），msg 为空/null 时才回退到本地化的"查询失败"。
                        // 卡片中心的错误文案由 XAML 绑定 member.StatusText 直接渲染。
                        var failText = !string.IsNullOrWhiteSpace(loaded.FailMsg)
                            ? loaded.FailMsg!
                            : L("TeamInfo.QueryFailed", "查询失败");
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            member.StatusText = failText;
                        });
                        // 不 return：让 final cleanup 块执行，确保 IsLoading 被重置
                    }
                    else
                    {
                        // 成功后缓存 ctx，供后续筛选器变更复用（避免重复 search/player）。
                        member.SourceContext = loaded.SourceContext;
                        // Step 6: 合并所有属性更新为单一批，降低 UI 线程队列压力
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            member.Level = loaded.Level;
                            // UID 一律不写回：member.UID 是语音日志给出的原始 UID（本地卡为 PlayerId），
                            // 是 UpdateTeamMembersAsync 里"按 recognizedSet 判定是否移除 / 已加载则跳过"的匹配 key。
                            // 一旦覆盖成后端返回的纯数字 RoleIdSimple，下一次触发时该卡会被误判为"已退出"而移除重建，
                            // 导致同一批队友反复走完整 search→player→season（重复 HTTP 根因）。后端纯数字仅供内部查询，
                            // 已通过 PlayerSourceContext.RoleIdSimple 传入 loader，无需写回成员卡；且 UI 不展示 UID。
                            // 后端真实昵称只写到 DisplayName（头像下展示），不碰 UserName（搜索框）以免回填
                            if (!string.IsNullOrWhiteSpace(loaded.UserName))
                                member.DisplayName = loaded.UserName;
                            member.AvatarUrl = loaded.AvatarUrl;
                            member.SoloRankScore = loaded.SoloRankScore;
                            member.DuoRankScore = loaded.DuoRankScore;
                            member.TrioRankScore = loaded.TrioRankScore;
                            member.KillCount = loaded.Stats?.AvgKill ?? member.KillCount;
                            member.Top5Rate = loaded.Stats?.Top5Rate ?? member.Top5Rate;
                            member.DamagePlayer = loaded.Stats?.AvgDamage ?? member.DamagePlayer;
                            member.SurviveTime = loaded.Stats?.SurviveTime ?? member.SurviveTime;
                            member.RankName = loaded.Stats?.RankName ?? member.RankName;
                            member.RankIcon = loaded.Stats?.RankIcon ?? member.RankIcon;
                            member.RankScore = loaded.Stats?.RankScore ?? 0;
                            member.PageRankName = loaded.Stats?.PageRankName ?? member.PageRankName;
                            member.PageStarCount = loaded.Stats?.PageStarCount ?? 0;
                            member.PageHasStars = loaded.Stats?.PageHasStars ?? false;
                            member.RankTierScore = loaded.Stats?.RankTierScore ?? 0;
                            member.Stats.Clear();
                            member.Metrics.Clear();
                            if (loaded.Stats != null)
                            {
                                foreach (var kv in loaded.Stats.Stats)
                                    member.Stats[kv.Key] = kv.Value;
                                member.Metrics.AddRange(loaded.Stats.Metrics);
                            }
                            member.StatusText = "";
                        });
                    }
                }
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
                AppLog.Error(ex, "TeamInfo", "Load member error");
                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.StatusText = L("TeamInfo.QueryFailed", "查询失败");
                });
            }
            // Final cleanup: 合并为单一批，避免 UI 线程队列堆积
            try
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    member.IsLoading = false;
                    // 成员数据到手后再校正一次本地用户居中：UpdateTeamMembersAsync 里 Add 新成员
                    // 会追加到末尾、移除又打乱顺序，首次 ReorderMembersForLocalUser 之后
                    // TeamMembers 顺序可能不再稳定。此处每格加载完补一次重排，
                    // 保证三排本地用户最终稳定落在中间卡片（index 1），与查询成功与否无关。
                    ReorderMembersForLocalUser();
                    UpdateDiffs();
                    IsLoading = TeamMembers.Any(m => m.IsLoading);
                    RaiseMemberProperties();

                    // 所有成员数据加载完毕时显示覆盖层提示框。
                    // 仅英雄选择阶段弹窗；游戏中打开程序识别到队友时直接展示页面，不弹窗打扰。
                    if (TeamMembers.Count >= 2 && TeamMembers.All(m => !m.IsLoading) && !_overlayShownForThisRound && !_overlayDismissedThisRound && IsHeroSelectionPhase)
                    {
                        _overlayShownForThisRound = true;
                        _teamDataLoadedSuccessfully = true;
                        _teamOverlayService.Show(BuildOverlayMembers());
                        _teamOverlayService.RefreshAction = RefreshFromOverlay;
                    }
                    // 弹窗已显示后，每格数据加载完毕时刷新弹窗中队名/段位等最新数据。
                    else if (_overlayShownForThisRound)
                    {
                        UpdateOverlayMembers();
                    }
                });
                _teamDataLoadedSuccessfully = true;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "TeamInfo", "Final cleanup error");
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

        /// <summary>
        /// 把只重查 season 得到的 stats 结果写回成员卡（不碰 identity/资料字段）。
        /// 供 <see cref="LoadMemberDataAsync"/> 的"复用 ctx"分支复用，避免与全量加载的写回逻辑重复。
        /// </summary>
        private async Task ApplyMemberStatsAsync(TeamMemberInfo member, PlayerStatsLoadResult? stats)
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                member.KillCount = stats?.AvgKill ?? member.KillCount;
                member.Top5Rate = stats?.Top5Rate ?? member.Top5Rate;
                member.DamagePlayer = stats?.AvgDamage ?? member.DamagePlayer;
                member.SurviveTime = stats?.SurviveTime ?? member.SurviveTime;
                member.RankName = stats?.RankName ?? member.RankName;
                member.RankIcon = stats?.RankIcon ?? member.RankIcon;
                member.RankScore = stats?.RankScore ?? 0;
                member.PageRankName = stats?.PageRankName ?? member.PageRankName;
                member.PageStarCount = stats?.PageStarCount ?? 0;
                member.PageHasStars = stats?.PageHasStars ?? false;
                member.RankTierScore = stats?.RankTierScore ?? 0;
                member.Stats.Clear();
                member.Metrics.Clear();
                if (stats != null)
                {
                    foreach (var kv in stats.Stats)
                        member.Stats[kv.Key] = kv.Value;
                    member.Metrics.AddRange(stats.Metrics);
                }
                member.StatusText = "";
            });
        }

        private List<TeamOverlayMemberItem> BuildOverlayMembers()
        {
            return TeamMembers.Select(m => new TeamOverlayMemberItem
            {
                UserName = ResolveDisplayName(m),
                AvatarUrl = m.AvatarUrl,
                RankName = m.RankName,
                RankIcon = m.RankIcon,
                PageRankName = m.PageRankName,
                PageStarCount = m.PageStarCount,
                PageHasStars = m.PageHasStars,
                RankTierScore = m.RankTierScore,
                IsLoading = m.IsLoading
            }).ToList();
        }

        private void UpdateOverlayMembers()
        {
            if (_overlayDismissedThisRound) return;
            if (TeamMembers.Count < 2) return;
            // 仅英雄选择阶段弹窗；游戏中打开程序识别到队友时直接展示页面，不弹窗。
            if (!IsHeroSelectionPhase) return;
            if (!_overlayShownForThisRound)
            {
                _overlayShownForThisRound = true;
                _teamOverlayService.RefreshAction = RefreshFromOverlay;
            }

            _teamOverlayService.Show(BuildOverlayMembers());
        }

        /// <summary>
        /// 展示名：优先后端查得的真实昵称（DisplayName），未加载完成（队友卡初始 UserName=UID）时
        /// 回退到 UserName。避免弹窗直接暴露队友 UID。
        /// </summary>
        private static string ResolveDisplayName(TeamMemberInfo m)
        {
            if (!string.IsNullOrWhiteSpace(m.DisplayName)) return m.DisplayName;
            return m.UserName;
        }

        /// <summary>
        /// 筛选器变更防抖到期后的实际重查回调。<paramref name="debounceCt"/> 仅代表"到期前是否被
        /// 下一次筛选变更取代"；成员 HTTP 加载另用 <see cref="_refreshMembersCts"/>（导航离开/刷新时取消），
        /// 两者语义独立，不可混用。
        /// </summary>
        private async Task RunFilterRefreshAsync(CancellationToken debounceCt)
        {
            if (debounceCt.IsCancellationRequested) return;

            CancelAndDispose(ref _refreshMembersCts);
            _refreshMembersCts = new CancellationTokenSource();
            var ct = _refreshMembersCts.Token;
            await RefreshMembersAsync(ct).ConfigureAwait(false);
        }

        private async Task RefreshMembersAsync(CancellationToken ct)
        {
            var members = TeamMembers.ToList();

            // 在 UI 线程上设置加载状态，确保 WPF 绑定跨线程安全
            // 不跳过无 UID 的成员：筛选器变更取消了首次加载时这些成员从未获得 UID，
            // 若此处跳过则它们永远不会被重试。
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var member in members)
                {
                    member.IsLoading = true;
                    member.StatusText = string.Empty;
                }
            });

            var tasks = new List<Task>();
            foreach (var member in members)
            {
                // 每个成员使用独立 linked token，互不干扰
                tasks.Add(LoadWithIndependentToken(member, ct));
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
            RaisePropertyChanged(nameof(Member0Ready));
            RaisePropertyChanged(nameof(Member1Ready));
            RaisePropertyChanged(nameof(Member2Ready));
            // 订阅每个成员的 HasStatusError 变化，实时刷新 Ready 属性，
            // 避免 StatusText 变更后 UI 未同步导致查询失败态仍显示旧数据行。
            for (int i = 0; i < TeamMembers.Count; i++)
            {
                var idx = i;
                var m = TeamMembers[i];
                m.PropertyChanged -= OnMemberPropertyChanged;
                m.PropertyChanged += OnMemberPropertyChanged;
            }
        }

        private void OnMemberPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TeamMemberInfo.HasStatusError)
                && e.PropertyName != nameof(TeamMemberInfo.StatusText)) return;
            RaisePropertyChanged(nameof(Member0Ready));
            RaisePropertyChanged(nameof(Member1Ready));
            RaisePropertyChanged(nameof(Member2Ready));
        }

        private static void ClearImageMemoryCaches()
        {
            // 清除 UrlToImageSourceConverter 的 BitmapImage 缓存
            UrlToImageSourceConverter.ClearCache();
            // 强制第 2 代 GC 回收，确保 WPF 非托管 BitmapImage 解码内存被释放
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.CancelAndDispose", "Cancel failed");
            }
            cts.Dispose();
            cts = null;
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            // 基类签名为 void，非事件 handler，禁止误标 async；页面初始化仅触发 fire-and-forget 异步任务即可。
            base.OnNavigatedToExecute(navigationContext);

            // 事件订阅已在构造函数中永久完成，页面导航不再重复订阅
            _ = LoadSeasonsAsync();
            // 注：LoadSeasonsAsync 内部已做"已成功加载则跳过"防抖；
            // 高频导航不会反复触发 HTTP 请求与 in-flight state machine 堆积。

            if (_gameStatusMonitor.CurrentStatus == GameStatus.HeroSelection)
            {
                IsHeroSelectionPhase = true;
                _overlayShownForThisRound = false;
                StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                StartMonitor();
            }
            else if (_gameStatusMonitor.CurrentStatus == GameStatus.InGame)
            {
                // 游戏中打开软件 / 进入页面：尝试从当前语音日志读取已写入的队友 UID 并展示。
                // 英雄选择阶段已识别到队友时 TeamMembers 非空，直接保留；
                // 若是中途打开（对局已在进行），Monitor.Reset+Start 会回放当前 m*.log 中已有的
                // set-uid-vol，从而拿到当前队友。
                IsHeroSelectionPhase = false;
                if (TeamMembers.Count == 0)
                {
                    _overlayShownForThisRound = false;
                    StartMonitor();
                }
                else
                {
                    StatusText = string.Empty;
                    _teamOverlayService.Hide();
                }
            }
            else
            {
                // 非英雄选择状态时清除旧队友数据，避免复杂UI（DropShadowEffect + 索引绑定）立即渲染导致卡死
                _hasEverHadData = false;
                TeamMembers.Clear();
                MergedStatRows.Clear();
                RaiseMemberProperties();

                IsHeroSelectionPhase = false;
                StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
            }
        }

        private async Task LoadSeasonsAsync()
        {
            // 已成功加载过赛季列表：直接复用现有数据，不再发起新请求。
            // 否则 N 次导航 → N 次重复 HTTP 调用，dump 中会残留多个 in-flight state machine。
            if (_seasonsLoaded) return;

            // 上一次还未完成就再次进入：取消旧请求，避免并发 HTTP 与并发写 Seasons。
            CancelAndDispose(ref _loadSeasonsCts);
            _loadSeasonsCts = new CancellationTokenSource();
            var ct = _loadSeasonsCts.Token;

            try
            {
                // 与战绩页共用同一赛季目录（前端内嵌，后端无 seasons 接口）；索引 0 为当前赛季。
                var seasons = SeasonCatalog.All();
                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    Seasons.Clear();
                    foreach (var s in seasons)
                        Seasons.Add(s);
                    if (Seasons.Count > 0 && _selectedSeason == null)
                        _selectedSeason = Seasons[0];
                    RaisePropertyChanged(nameof(SelectedSeason));
                    _seasonsLoaded = true;
                });
            }
            catch (OperationCanceledException)
            {
                // 离开页面或被新请求取代——正常路径，不报错。
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.{nameof(LoadSeasonsAsync)}", "Load seasons error");
            }
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            // 离开页面时清理页面级操作和事件订阅。
            // _gameStatusMonitor.GameStatusRecognized / _teamOverlayService.Dismissed / _teammateMonitor.TeammatesReady
            // 是构造函数中永久订阅的，离开页面不解绑——后台监听和右下角弹窗在任意页面都需要正常工作。
            // 但 _teamOverlayService.RefreshAction 持有 VM 的方法回调（RefreshFromOverlay），
            // overlay service 是单例，必须在此清空，防止 VM 通过委托被外部单例长期持引用。
            // 注意：日志监听由游戏状态驱动，不在此处 Stop（否则离开 TeamInfo 页面后后台识别会中断）。
            _teamOverlayService.RefreshAction = null;

            CancelAndDispose(ref _refreshMembersCts);
            CancelAndDispose(ref _refreshOcrCts);
            // _loadSeasonsCts 也要在离开页面时取消：留它在 in-flight 不会立即崩，但下次再进页面
            // 会被新的 CancelAndDispose 替换，相当于多保留一个不必要的 HTTP/state machine 引用。
            CancelAndDispose(ref _loadSeasonsCts);
            base.OnNavigatedFromExecute(navigationContext);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gameStatusMonitor.GameStatusRecognized -= OnGameStatusRecognized;
                _teamOverlayService.Dismissed -= OnOverlayDismissed;
                _teammateMonitor.TeammatesReady -= OnTeammatesReady;
                _filterRefreshDebouncer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 队友卡片的单行合并数据：标签 + 3 个成员的值 + 左右 diff。
    /// MergedStatRows 绑定单个 ItemsControl，每行模板是 5 列 Grid，天然水平对齐。
    /// </summary>
    public class MergedStatRow
    {
        public string Label { get; set; } = string.Empty;
        public string Val0 { get; set; } = "-";
        public string Val1 { get; set; } = "-";
        public string Val2 { get; set; } = "-";
        public string DiffLeftText { get; set; } = string.Empty;
        public string DiffLeftColor { get; set; } = "#999999";
        public string DiffRightText { get; set; } = string.Empty;
        public string DiffRightColor { get; set; } = "#999999";
    }

    public class TeamMemberInfo : ViewModelBase
    {
        private readonly IClipboardService? _clipboard;
        private readonly ILocalizedTextProvider? _localizedText;
        private readonly ITipMessageService? _tipMessage;

        /// <summary>
        /// 默认 ctor 仅供 d:DesignInstance 等设计时反射用；运行时所有 TeamMemberInfo 必须走 ctor(IClipboardService, ILocalizedTextProvider, ITipMessageService)。
        /// </summary>
        public TeamMemberInfo() { }

        public TeamMemberInfo(IClipboardService clipboard, ILocalizedTextProvider localizedText, ITipMessageService tipMessage)
        {
            _clipboard = clipboard;
            _localizedText = localizedText;
            _tipMessage = tipMessage;
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

        // 本地用户格（中间卡片）标志：三排/双排里中间格恒为本地用户。
        private bool _isLocalUser;
        public bool IsLocalUser
        {
            get => _isLocalUser;
            set
            {
                if (_isLocalUser == value) return;
                _isLocalUser = value;
                RaisePropertyChanged(nameof(IsLocalUser));
            }
        }

        // 头像下方展示用的玩家名：与搜索框绑定的 UserName 解耦，
        // 后端返回的真实昵称写到这里而不回填搜索框。
        private string _displayName = string.Empty;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value) return;
                _displayName = value;
                RaisePropertyChanged(nameof(DisplayName));
            }
        }

        // 已解析出的玩家查询上下文（roleIdSimple + 数据源）。首次 search+player 成功后写入；
        // 后续筛选器变更重查 season 时直接复用，避免对同一玩家重复 search/player。
        // 未解析前为 null（对应"整卡未加载/失败"）。
        private PlayerSourceContext? _sourceContext;
        public PlayerSourceContext? SourceContext
        {
            get => _sourceContext;
            set
            {
                if (_sourceContext == value) return;
                _sourceContext = value;
                RaisePropertyChanged(nameof(SourceContext));
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

        // 后端返回的 metric 有序列表（含标签），本地用户格用它作为三栏统一行模板。
        public System.Collections.Generic.List<BlackGoldAncientSword.Modules.UI.TeamInfo.Services.PlayerStatMetric> Metrics { get; } = new();

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

        public System.Action<TeamMemberInfo>? NavigateToStatsAction { get; set; }

        private DelegateCommand? _navigateToStatsCommand;
        public DelegateCommand NavigateToStatsCommand =>
            _navigateToStatsCommand ??= new DelegateCommand(() =>
            {
                NavigateToStatsAction?.Invoke(this);
            });

    }
}






