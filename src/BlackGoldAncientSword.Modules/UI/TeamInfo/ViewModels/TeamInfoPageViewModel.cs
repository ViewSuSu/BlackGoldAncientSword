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
        private readonly ITipMessageService _tipMessage;
        private CancellationTokenSource? _ocrLoopCts;
        private bool _isOcrRunning;
        private readonly object _ocrLock = new();
        private bool _isHeroSelectionPhase;
        private CancellationTokenSource? _refreshMembersCts;
        private CancellationTokenSource? _refreshOcrCts;
        private CancellationTokenSource? _loadSeasonsCts;
        private readonly SearchDebounceGate _refreshOcrDebounce = new();
        // 已成功加载赛季后不再重复请求；后续切换页面也复用既有数据。
        private bool _seasonsLoaded;
        private bool _hasEverHadData;
        private bool _overlayShownForThisRound;
        private bool _overlayDismissedThisRound;
        /// <summary>
        /// 标记 OCR 是否已成功完成至少一轮完整识别（识别 + 数据加载）。
        /// 用于 <see cref="StartOcrLoop"/> 的保护性判断：已有有效数据时不再重复启动轮询循环，
        /// 但用户仍可通过刷新按钮手动触发单次重新识别。
        /// 在游戏状态变为 Unknown 时重置。
        /// </summary>
        private bool _ocrDataLoadedSuccessfully;

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
            ILocalizedTextProvider localizedText,
            ITipMessageService tipMessage)
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
            _tipMessage = tipMessage;

            TeamMembers = new ObservableCollection<TeamMemberInfo>();
            Seasons = new ObservableCollection<UnifiedSeason>();
            MergedStatRows = new ObservableCollection<MergedStatRow>();
            _selectedTeamSize = TeamSize.Trio;
            _selectedCategory = GameModeCategory.Rank;
            // _statusText 不能用字段初始化器调 L()，因为 _localizedText 此时还未注入。
            _statusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");

            // 构造函数中永久订阅（不依赖页面导航），确保进入英雄选择后立即启动 OCR 识别
            _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;
            _teamOverlayService.Dismissed += OnOverlayDismissed;
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
                if (!_refreshOcrDebounce.TryEnter())
                {
                    _tipMessage.ShowError(L("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }
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
                "VM", $"OnGameStatusRecognized status={args.Status}, dataLoaded={_ocrDataLoadedSuccessfully}");

            // 非英雄选择状态时立即取消 OCR 循环，无需等待 UI 线程调度。
            // GameLogMonitor 在 ThreadPool 上触发事件，HandleGameStatusOnUiThread 通过
            // _uiDispatcher.InvokeAsync marshal 到 UI 线程，从事件触发到实际执行 StopOcrLoop
            // 之间存在延迟窗口，OCR 截图循环在此期间会继续执行。
            // StopOcrLoop 内部持有 _ocrLock，线程安全，可从任意线程调用。
            if (args.Status != GameStatus.HeroSelection)
            {
                StopOcrLoop();
            }

            // 英雄选择状态下已有有效数据时，不启动 OCR 循环
            if (args.Status == GameStatus.HeroSelection && _ocrDataLoadedSuccessfully)
            {
                if (!_uiDispatcher.CheckAccess())
                {
                    _ = _uiDispatcher.InvokeAsync(() =>
                    {
                        IsHeroSelectionPhase = true;
                        StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                    });
                    return;
                }
                IsHeroSelectionPhase = true;
                StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                return;
            }

            // HandleGameStatusOnUiThread 中 InGame/BattleEnded/Unknown 分支仍会调用
            // StopOcrLoop()，但 _isOcrRunning 已为 false 时会通过 lock 内早期 return 快速退出。

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
                    _ocrDataLoadedSuccessfully = false;
                    StatusText = L("TeamInfo.HeroSelectRecognizing", "英雄选择中，正在识别队友...");
                    StartOcrLoop();
                    break;
                case GameStatus.InGame:
                    IsHeroSelectionPhase = false;
                    _teamOverlayService.Hide();
                     StopOcrLoop();
                     CancelAndDispose(ref _refreshOcrCts);
                    ClearImageMemoryCaches();
                    break;
                case GameStatus.BattleEnded:
                    IsHeroSelectionPhase = false;
                    _ocrDataLoadedSuccessfully = false;
                    _teamOverlayService.Hide();
                     StopOcrLoop();
                     CancelAndDispose(ref _refreshOcrCts);
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
                     StopOcrLoop();
                    StatusText = L("TeamInfo.WaitingForHeroSelect", "等待游戏进入英雄选择...");
                     if (TeamMembers.Count > 0)
                    ClearImageMemoryCaches();
                     {
                         _hasEverHadData = false;
                         _ocrDataLoadedSuccessfully = false;
                         TeamMembers.Clear();
                         MergedStatRows.Clear();
                         RaiseMemberProperties();
                     }
                    break;
            }
        }

        private void StartOcrLoop()
        {
            BlackGoldAncientSword.Framework.Core.Infrastructure.DiagLog.Write(
                "VM", $"StartOcrLoop 入口, dataLoaded={_ocrDataLoadedSuccessfully}, isRunning={_isOcrRunning}");

            // 如果上一轮已成功完成完整识别（有 UID、数据已加载），不再重复启动 OCR 轮询。
            // 用户仍可通过右上角的刷新按钮手动触发单次识别（RefreshOcrCommand → RefreshOcrOnceAsync）。
            if (_ocrDataLoadedSuccessfully)
            {
                Debug.WriteLine($"[{nameof(TeamInfoPageViewModel)}] Skip OCR loop: valid data already loaded.");
                return;
            }

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

                var names = await _ocrCoordinator.RecognizeAutoAsync(ct);
                ct.ThrowIfCancellationRequested();

                if (names.Length == 0) return;

                // 用本地已知昵称 (player_prefs.txt) 校正 OCR 结果中"最像自己"的那一格。
                // 只改自己那一格，队友格永不动。命中率提升让下游 ReorderMembersForLocalUser
                // 能稳定把自己放到中间卡片。
                names = TeamMemberNameCorrector.Apply(names, _playerPrefsService.Current.OriginalPlayerName);

                // 智能去重：本轮 OCR 名字与当前 TeamMembers 比对——
                // - 新增名字：加入 TeamMembers，加入请求列表
                // - 已存在且已成功加载（有 UID + stats + 无错误）：跳过，不重发 HTTP（省后端压力）
                // - 已存在但上次失败/未加载：重置状态并加入请求列表（允许用户通过刷新按钮重试）
                // - 本轮未识别到的旧成员：从 TeamMembers 移除
                var toLoad = new List<TeamMemberInfo>();
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

                        var member = new TeamMemberInfo(_clipboard, _localizedText, _tipMessage)
                        {
                            UserName = name,
                            IsLoading = true,
                            RefreshAction = RefreshSingleMember,
                            NavigateToStatsAction = NavigateToMemberStats
                        };
                        TeamMembers.Add(member);
                        toLoad.Add(member);
                    }

                    // 对已存在的成员判断是否需要重新请求
                    foreach (var member in TeamMembers)
                    {
                        if (toLoad.Contains(member)) continue; // 上面新增的已经登记

                        var alreadyLoaded = !string.IsNullOrEmpty(member.UID)
                                            && member.Stats.Count > 0
                                            && !member.HasStatusError;
                        if (alreadyLoaded) continue; // 已成功加载 → 跳过 HTTP

                        // 上次失败或未加载完：重置状态并加入本轮请求列表
                        member.IsLoading = true;
                        member.StatusText = string.Empty;
                        toLoad.Add(member);
                    }

                    ReorderMembersForLocalUser();
                    RaiseMemberProperties();
                });

                // 只对需要请求的成员启动 LoadWithIndependentToken
                if (toLoad.Count > 0)
                {
                    var tasks = new List<Task>();
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        foreach (var member in toLoad)
                        {
                            tasks.Add(LoadWithIndependentToken(member, ct));
                        }
                    });
                    await Task.WhenAll(tasks);
                }
                _ocrDataLoadedSuccessfully = true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[TeamInfo] Refresh OCR cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "TeamInfo", "Refresh OCR error");
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
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.{nameof(RefreshFromOverlay)}");
            }
        }

        private async Task OcrLoopAsync(CancellationToken ct)
        {
            try
            {
                // retryInterval=Zero 时会变成"识别失败立刻重试"的零休眠死循环：
                // 一旦英雄选择阶段 OCR 拿不到队友名（例如分辨率不匹配、UI 还没绘出），
                // 每秒会反复触发 3 次 PaddleOCR 推理 + 全屏抓取，把 CPU 与磁盘打满。
                // 1500 ms 在人类感知上仍近乎立刻，但 CPU 占用相比零休眠降一个数量级。
                var names = await _ocrCoordinator.WaitForAutoRecognitionAsync(
                    initialDelay: TimeSpan.Zero,
                    retryInterval: TimeSpan.FromMilliseconds(1500),
                    ct);

                if (ct.IsCancellationRequested || names.Length == 0) return;

                // ★ 一旦 OCR 识别到有效队友名，立即停止后续 OCR 截图。
                // 即使 HTTP 数据加载失败，也不应重复截图识别。
                _ocrDataLoadedSuccessfully = true;

                // ★ 如果游戏状态已非英雄选择阶段，跳过本次识别结果处理。
                // 避免 HeroSelection→InGame 转换延迟窗口中最后一次截图结果被错误处理。
                if (_gameStatusMonitor.CurrentStatus != GameStatus.HeroSelection)
                    return;

                // 确保赛季数据已加载后再加载成员数据，避免 _selectedSeason?.Code 为 null
                await LoadSeasonsAsync();

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
                AppLog.Error(ex, "TeamInfo", "OCR loop error");
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
                AppLog.Error(ex, $"{nameof(TeamInfoPageViewModel)}.{nameof(SafeUpdateTeamMembers)}");
            }
        }

        private async Task UpdateTeamMembersAsync(string[] names, CancellationToken ct)
        {
            // 用本地已知昵称 (player_prefs.txt) 校正 OCR 结果中"最像自己"的那一格。
            // 只改自己那一格，队友格永不动。校正后再做 identical-skip 判断，避免
            // 上次已校正的 TeamMembers 与本轮未校正的 names 比较时出现假差异。
            names = TeamMemberNameCorrector.Apply(names, _playerPrefsService.Current.OriginalPlayerName);

            // Skip if recognized names are identical to current members
            if (names.Length == TeamMembers.Count &&
                names.All(n => TeamMembers.Any(m =>
                    string.Equals(m.UserName, n, StringComparison.OrdinalIgnoreCase))))
                return;

            // Mark that we have loaded data at least once
            _hasEverHadData = true;

            // 给成员数据加载用独立 token，不复用 OCR loop 的 ct。
            // 原因：游戏状态 HeroSelection → InGame 切换会 StopOcrLoop()→取消 _ocrLoopCts，
            // 此时若 stats HTTP 仍在 in-flight 会抛 OperationCanceledException，
            // LoadMemberDataAsync 跳过字段写入 block，卡片出现"段位有数据但 stats 全空"的不一致状态。
            // member-scoped 加载只应在用户主动整队刷新（RefreshTeamMemberData）或离开页面时取消。
            if (_refreshMembersCts == null || _refreshMembersCts.IsCancellationRequested)
            {
                CancelAndDispose(ref _refreshMembersCts);
                    _refreshMembersCts = new CancellationTokenSource();
            }
            var loadCt = _refreshMembersCts.Token;

            var existingNames = TeamMembers.Select(m => m.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newNames = names.Where(n => !existingNames.Contains(n)).ToArray();

            var recognizedSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = TeamMembers.Where(m => !recognizedSet.Contains(m.UserName)).ToList();
            foreach (var r in removed)
                TeamMembers.Remove(r);

            foreach (var name in newNames)
            {
                ct.ThrowIfCancellationRequested();
                var member = new TeamMemberInfo(_clipboard, _localizedText, _tipMessage)
                {
                    UserName = name,
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
        }

        private void NavigateToMemberStats(string userName)
        {
            var parameters = new NavigationParameters
            {
                { NavigationParameterKeys.TargetPlayerName, userName }
            };
            _navigation.NavigateTo(PageNames.StatsPage, parameters);
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
            // For 2 members: local user goes to center (index 1) — matching the blue border on Member1
            else if (TeamMembers.Count == 2)
            {
                if (localIdx != 1)
                {
                    TeamMembers.Move(localIdx, 1);
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

        // 与战绩页「数据详情」字段完全对齐的 16+1 个字段，顺序与 heyBox overview 一致。
        private static readonly (string Key, string Label, bool IsPercent)[] StatDefs =
        {
            ("round",              "总场次",     false),
            ("win_rate",           "夺冠率",     true),
            ("top5_rate",          "前五率",     true),
            ("avg_damage",         "场均伤害",   false),
            ("dmg_per_kill",       "伤害/击杀",  false),
            ("win",                "夺冠",       false),
            ("top5",               "前五",       false),
            ("kd",                 "K/D",        false),
            ("max_damage",         "最高伤害",   false),
            ("max_cure",           "最高恢复",   false),
            ("max_kill",           "最高击杀",   false),
            ("total_time",         "总对局时间", false),
            ("avg_shock",          "场均振刀",   false),
            ("avg_cure",           "场均恢复",   false),
            ("avg_kill",           "场均击杀",   false),
            ("avg_total_live_time","场均存活时间",false),
            ("__rank__",           "段位分",     false),
        };

        /// <summary>
        /// 重建 MergedStatRows：按 StatDefs 顺序每行合并 3 个成员的值 + 2 列 diff。
        /// 一个集合绑定一个 ItemsControl，每行模板是 5 列 Grid，天然水平对齐。
        /// </summary>
        private void UpdateDiffs()
        {
            MergedStatRows.Clear();

            var m0 = TeamMembers.Count > 0 ? TeamMembers[0] : null;
            var m1 = TeamMembers.Count > 1 ? TeamMembers[1] : null;
            var m2 = TeamMembers.Count > 2 ? TeamMembers[2] : null;

            var localIdx = LocalUserIndex;
            if (localIdx < 0) localIdx = 0;

            // diff 的两侧：本地用户 vs 左边队友、本地用户 vs 右边队友
            TeamMemberInfo? diffLeftA = null, diffLeftB = null;
            TeamMemberInfo? diffRightA = null, diffRightB = null;
            if (m0 != null && m1 != null)
            {
                diffLeftA = localIdx == 0 ? m0 : m1;
                diffLeftB = localIdx == 0 ? m1 : m0;
            }
            if (m1 != null && m2 != null)
            {
                diffRightA = localIdx == 1 ? m1 : (localIdx == 2 ? m2 : m1);
                diffRightB = localIdx == 1 ? m2 : (localIdx == 2 ? m1 : m2);
            }

            foreach (var def in StatDefs)
            {
                var row = new MergedStatRow { Label = def.Label };

                row.Val0 = GetStatVal(m0, def.Key);
                row.Val1 = GetStatVal(m1, def.Key);
                row.Val2 = GetStatVal(m2, def.Key);

                if (diffLeftA != null && diffLeftB != null)
                    FillDiff(row, isLeft: true, diffLeftA, diffLeftB, def);
                if (diffRightA != null && diffRightB != null)
                    FillDiff(row, isLeft: false, diffRightA, diffRightB, def);

                MergedStatRows.Add(row);
            }

            RaisePropertyChanged(nameof(HasDiffLeft));
            RaisePropertyChanged(nameof(HasDiffRight));
        }

        private static string GetStatVal(TeamMemberInfo? m, string key)
        {
            if (m == null) return "-";
            if (key == "__rank__") return m.RankScore > 0 ? m.RankScore.ToString("F0") : "-";
            return m.Stats.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : "-";
        }

        private static void FillDiff(MergedStatRow row, bool isLeft,
            TeamMemberInfo a, TeamMemberInfo b,
            (string Key, string Label, bool IsPercent) def)
        {
            double av, bv;
            if (def.Key == "__rank__")
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
                // Step 1-4: 调 Loader 在后台线程拉数据；返回 DTO 后回 UI 线程逐批写回属性。
                var loaded = await _memberLoader.LoadAsync(
                    userName,
                    _selectedSeason?.Code,
                    _selectedCategory,
                    _selectedTeamSize,
                    ct).ConfigureAwait(false);

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
                    // Step 6: 合并所有属性更新为单一批，降低 UI 线程队列压力
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        member.Level = loaded.Level;
                        member.UID = loaded.UID;
                        member.AvatarUrl = loaded.AvatarUrl;
                        member.SoloRankScore = loaded.SoloRankScore;
                        member.DuoRankScore = loaded.DuoRankScore;
                        member.TrioRankScore = loaded.TrioRankScore;
                        member.KillCount = loaded.Stats?.AvgKill ?? member.KillCount;
                        member.Top5Rate = loaded.Stats?.Top5Rate ?? member.Top5Rate;
                        member.DamagePlayer = loaded.Stats?.AvgDamage ?? member.DamagePlayer;
                        member.SurviveTime = loaded.Stats?.SurviveTime ?? member.SurviveTime;
                        var s = loaded.Stats;
                        member.RankName = s?.RankName ?? member.RankName;
                        member.RankIcon = s?.RankIcon ?? member.RankIcon;
                        member.RankScore = s?.RankScore ?? 0;
                        member.PageRankName = s?.PageRankName ?? member.PageRankName;
                        member.PageStarCount = s?.PageStarCount ?? 0;
                        member.PageHasStars = s?.PageHasStars ?? false;
                        member.RankTierScore = s?.RankTierScore ?? 0;
                        member.Stats.Clear();
                        if (loaded.Stats != null)
                        {
                            foreach (var kv in loaded.Stats.Stats)
                                member.Stats[kv.Key] = kv.Value;
                        }
                        member.StatusText = "";
                    });
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
                    UpdateDiffs();
                    IsLoading = TeamMembers.Any(m => m.IsLoading);
                    RaiseMemberProperties();

                    // 所有成员数据加载完毕时显示覆盖层提示框
                    if (TeamMembers.Count >= 2 && TeamMembers.All(m => !m.IsLoading) && !_overlayShownForThisRound && !_overlayDismissedThisRound)
                    {
                        _overlayShownForThisRound = true;
                        _ocrDataLoadedSuccessfully = true;
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
                _ocrDataLoadedSuccessfully = true;
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
            // Task.Run 将整个刷新流程推到 ThreadPool，避免 UI 线程上的同步启动开销
            _ = Task.Run(() => RefreshMembersAsync(ct), ct);
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
                StartOcrLoop();
            }
            else if (_gameStatusMonitor.CurrentStatus == GameStatus.InGame
                     && TeamMembers.Count > 0)
            {
                // 游戏中且英雄选择阶段已识别到队友：整页保留，不清理。
                // 只要识别到成员就保留，即使部分成员查询失败（失败卡片自带错误覆盖层展示 msg），
                // 也不因个别失败把整页打回"等待进入英雄选择"的转圈态。
                IsHeroSelectionPhase = false;
                StatusText = string.Empty;
                _teamOverlayService.Hide();
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
                var seasonsResp = await NarakaApiClient.QuerySeasonsAsync().ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                var seasons = UnifiedMapper.MapSeasons(seasonsResp);
                if (seasons.Count > 0)
                {
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
            // _gameStatusMonitor.GameStatusRecognized / _teamOverlayService.Dismissed 是构造函数中永久订阅的，
            // 离开页面不解绑——后台 OCR 和右下角弹窗在任意页面都需要正常工作。
            // 但 _teamOverlayService.RefreshAction 持有 VM 的方法回调（RefreshFromOverlay），
            // overlay service 是单例，必须在此清空，防止 VM 通过委托被外部单例长期持引用。
            // 注意：OCR 循环由游戏状态驱动，不在此处 Stop（否则离开 TeamInfo 页面后后台识别会中断）。
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
        private readonly SearchDebounceGate _searchDebounce = new();

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

        private DelegateCommand<string>? _searchMemberCommand;
        public DelegateCommand<string> SearchMemberCommand =>
            _searchMemberCommand ??= new DelegateCommand<string>(input =>
            {
                if (!_searchDebounce.TryEnter())
                {
                    var tip = _localizedText?.Get("Search.TooFast", "点击过快请稍后重试") ?? "点击过快请稍后重试";
                    _tipMessage?.ShowError(tip);
                    return;
                }
                // 用搜索框里用户输入的名字更新 UserName，再触发搜索
                if (!string.IsNullOrWhiteSpace(input))
                    UserName = input.Trim();
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






