using System.Diagnostics;
using System.Text.RegularExpressions;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    /// <summary>
    /// CCMini 语音日志监控器实现。
    /// <para>
    /// 永劫无间每次启动会新建 <c>ccmini\ccmini_new\logs\mYYYYMMDDHHmmss.log</c> 语音日志。
    /// 进入对局（英雄选择阶段）连上队伍语音频道后，会立即写入若干行
    /// <c>set-uid-vol</c>（为每个队友设置单独音量），其中的 <c>uid</c> 即队友角色 ID。
    /// 该记录实时落盘，无需等游戏结束。
    /// </para>
    /// <para>
    /// 本类职责：定位日志目录 → 跟踪当前最新的 <c>m*.log</c> → 增量读取并解析
    /// <c>set-uid-vol</c> 的 uid → 排除本地用户、去重 → 数量达到期望阈值即触发
    /// <see cref="TeammatesReady"/>。FSW 监听目录内 <c>*.log</c> 新建/变更以切换文件；
    /// 复用 <see cref="LogPoller"/> 兜底轮询 + <see cref="LogReader"/> 增量读。
    /// </para>
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class CcMiniTeammateMonitor : ICcMiniTeammateMonitor
    {
        // set-uid-vol 行示例：
        // [2026-08-07 16:40:08:115] [SERVICE] JsonControl {"type": "set-uid-vol", "percent" : 100, "uid" : "2a5d000045516500130163", "session-id" : 2 }
        // 队伍语音频道固定为 session-id : 2；队友 UID 全部出现在该频道的 set-uid-vol 里。
        // 按行匹配（(?m) 多行模式 + [^\r\n] 限制在同一行内），避免跨行贪婪吞掉多行。
        private static readonly Regex SetUidVolRegex = new(
            @"(?m)""type""\s*:\s*""set-uid-vol""[^\r\n]*?uid""\s*:\s*""([a-zA-Z0-9]+)""[^\r\n]*?session-id""\s*:\s*(\d+)",
            RegexOptions.Compiled);

        // 队友角色 ID：实测固定 22 位字母数字（前缀字母/数字混合 0~4 位 + 18~22 位数字），
        // 含纯数字 UID（如 0189000041653400090163）。不能用"16 位数字结尾"之类的长度规则，会误杀。
        private static readonly Regex UidPattern = new(@"^[a-zA-Z0-9]{22}$", RegexOptions.Compiled);

        // 队伍语音频道 ID（login-session 与 set-uid-vol 的 session-id 均为 2）。
        private const string TeamSessionId = "2";

        // 最多保留的队友 UID 数（三排 = 2 名队友）。队伍规模由 VM 侧按实际数量判定：
        // 语音日志 set-uid-vol 只给队友设音量，故最多 2 个（三排）。
        private const int MaxTeammates = 2;

        // 收到新 UID 后等待此窗口再触发：让同局全部 set-uid-vol 都写盘，避免把
        // "三排第 2 个队友尚未落盘"误判成双排。三排 2 个 set-uid-vol 几乎同时写出（实测 1ms 内）。
        private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

        private readonly IPlayerPrefsService _playerPrefs;
        private readonly LogReader _reader = new();
        private readonly LogPoller _poller = new();

        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private readonly object _stateLock = new();
        private string? _currentLogPath;
        private string? _activeLogDir;
        private long _lastPosition;
        // 当前"最近活跃"的队友 UID 集合：按最近一次 set-uid-vol 出现时间排序（最新在前）。
        // 容量上限 MaxTeammates；队友退出/换人时新 UID 插入队首、最旧的被淘汰，从而反映当前队友。
        private readonly List<string> _recognizedUids = new();
        // 最近一次对外触发过的集合快照：内容不变时不再重复触发，避免对同一批 UID 反复发事件。
        private string _lastFiredSignature = string.Empty;
        private bool _hasFiredOnce;
        private bool _settleTaskStarted;
        private bool _running;

        public event EventHandler<CcMiniTeammatesEventArgs>? TeammatesReady;

        public IReadOnlyList<string> TeammateUids
        {
            get { lock (_stateLock) return _recognizedUids.ToList(); }
        }

        public bool HasRecognized
        {
            get { lock (_stateLock) return _hasFiredOnce; }
        }

        public CcMiniTeammateMonitor(IPlayerPrefsService playerPrefs)
        {
            _playerPrefs = playerPrefs;
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_running) return;
                _running = true;
            }

            var logDir = ResolveCcMiniLogDir();
            DiagLog.Write("CCM", $"Start: logDir={logDir}");
            AppLog.Info(nameof(CcMiniTeammateMonitor), $"Start: logDir={logDir}");
            if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
            {
                DiagLog.Write("CCM", "日志目录不存在，静默返回");
                AppLog.Warning(nameof(CcMiniTeammateMonitor), "CCMini 日志目录不存在，监控静默跳过");
                return;
            }

            // 切到目录内最新 m*.log（当前进行中的会话）。
            lock (_stateLock) { _activeLogDir = logDir; }
            AttachToLatestLog(logDir);

            _watcher = new FileSystemWatcher(logDir, "m*.log")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnLogChanged;
            _watcher.Created += OnLogCreated;

            _pollCts = new CancellationTokenSource();
            _pollTask = _poller.RunAsync(PollTickAsync, _pollCts.Token);
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                _running = false;
            }

            if (_pollCts != null)
            {
                try { _pollCts.Cancel(); }
                catch (Exception ex) { AppLog.Error(ex, nameof(CcMiniTeammateMonitor), "Cancel failed"); }
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnLogChanged;
                _watcher.Created -= OnLogCreated;
                _watcher.Dispose();
                _watcher = null;
            }

            if (_pollCts != null)
            {
                if (_pollTask != null)
                {
                    bool exited = false;
                    try { exited = _pollTask.Wait(TimeSpan.FromMilliseconds(500)); }
                    catch (Exception ex) { AppLog.Error(ex, nameof(CcMiniTeammateMonitor), "poll task wait failed"); }
                    if (!exited)
                    {
                        _pollCts = null;
                        _pollTask = null;
                        return;
                    }
                    _pollTask = null;
                }
                _pollCts.Dispose();
                _pollCts = null;
            }
        }

        public void Reset()
        {
            lock (_stateLock)
            {
                _recognizedUids.Clear();
                _hasFiredOnce = false;
                _lastFiredSignature = string.Empty;
                _settleTaskStarted = false;
                _lastPosition = 0;
                _currentLogPath = null;
                _activeLogDir = null;
            }
            DiagLog.Write("CCM", "Reset: 清空已识别 UID，等待新对局/重新回放");
        }

        public void Dispose()
        {
            Stop();
            _reader.Dispose();
        }

        /// <summary>
        /// 定位 CCMini 日志目录。
        /// <para>
        /// 永劫无间有 Steam 与网易两个客户端，各自独立的安装目录与 CCMini 日志
        /// （Steam: <c>...\NARAKA BLADEPOINT\ccmini\ccmini_new\logs</c>，
        /// 网易: <c>...\Naraka\program\ccmini\ccmini_new\logs</c>）。登录哪个客户端，
        /// 队友 UID 就写进哪份日志，绝不能读错。
        /// </para>
        /// <para>
        /// 策略：1) 优先从游戏进程 exe 路径推导（能精确区分客户端，但非管理员权限下 MainModule
        /// 可能被拒）；2) 失败则扫描已知客户端路径，选「最近有 m*.log 写入」的那个作为当前活跃客户端。
        /// </para>
        /// </summary>
        private static string? ResolveCcMiniLogDir()
        {
            // 1) 进程 exe 路径推导（最准，能覆盖自定义安装位置）。
            try
            {
                var procs = Process.GetProcessesByName("NarakaBladepoint");
                try
                {
                    foreach (var p in procs)
                    {
                        try
                        {
                            var exe = p.MainModule?.FileName;
                            if (string.IsNullOrEmpty(exe)) continue;
                            var gameRoot = Path.GetDirectoryName(exe);
                            if (string.IsNullOrEmpty(gameRoot)) continue;
                            var dir = Path.Combine(gameRoot, "ccmini", "ccmini_new", "logs");
                            if (Directory.Exists(dir)) return dir;
                        }
                        catch (Exception ex) { AppLog.Error(ex, nameof(CcMiniTeammateMonitor), "MainModule read failed"); }
                    }
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }
            catch (Exception ex) { AppLog.Error(ex, nameof(CcMiniTeammateMonitor), "ResolveCcMiniLogDir process check failed"); }

            // 2) 从注册表解析 Steam/网易客户端安装路径，选最近活跃（最后写入 m*.log 的）客户端。
            // Steam 与网易都装了时，以"哪个日志最新"为准——刚登录的客户端必然在写日志。
            var candidates = GameInstallLocator.ResolveAllCcMiniLogDirs();
            if (candidates.Count == 0)
            {
                AppLog.Warning(nameof(CcMiniTeammateMonitor), "无法从注册表解析到任何 CCMini 日志目录");
                return null;
            }

            string? bestDir = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var dir in candidates)
            {
                if (!Directory.Exists(dir)) continue;
                DateTime latest;
                try
                {
                    latest = Directory.GetFiles(dir, "m*.log")
                        .Where(f => !f.EndsWith("_cclib.log", StringComparison.OrdinalIgnoreCase))
                        .Select(f => new FileInfo(f).LastWriteTime)
                        .DefaultIfEmpty(DateTime.MinValue)
                        .Max();
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, nameof(CcMiniTeammateMonitor), $"Scan log dir failed: {dir}");
                    continue;
                }
                if (latest > bestTime)
                {
                    bestTime = latest;
                    bestDir = dir;
                }
            }

            if (bestDir != null)
                AppLog.Info(nameof(CcMiniTeammateMonitor), $"ResolveCcMiniLogDir 选择活跃客户端日志: {bestDir} (lastWrite={bestTime:yyyy-MM-dd HH:mm:ss})");
            return bestDir;
        }

        /// <summary>
        /// 切换到目录内最新 m*.log。仅在 <paramref name="forceReset"/> 为 true（新文件 / 主动 Reset）
        /// 或当前跟踪文件已不是最新（新会话出现）时重置读取位置，避免破坏增量进度。
        /// </summary>
        private void AttachToLatestLog(string logDir, bool forceReset = false)
        {
            string? latest = null;
            try
            {
                latest = Directory.GetFiles(logDir, "m*.log")
                    .Where(f => !f.EndsWith("_cclib.log", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(CcMiniTeammateMonitor), "AttachToLatestLog listing failed");
            }

            bool switched;
            lock (_stateLock)
            {
                switched = !string.Equals(_currentLogPath, latest, StringComparison.OrdinalIgnoreCase);
                if (switched)
                {
                    _currentLogPath = latest;
                    _lastPosition = 0;
                }
                else if (forceReset)
                {
                    // 主动 Reset 场景（Start / 新对局）：即使文件相同也清空进度与 UID 集合。
                    _lastPosition = 0;
                }
            }
            DiagLog.Write("CCM", $"AttachToLatestLog: {latest}, switched={switched}");
            if (switched || forceReset)
                AppLog.Info(nameof(CcMiniTeammateMonitor), $"AttachToLatestLog: {latest ?? "(无 m*.log)"}, switched={switched}");

            if (latest != null)
                ReadNewContent(latest);
        }

        private void OnLogCreated(object sender, FileSystemEventArgs e)
        {
            // 每局游戏启动新建 m*.log（新会话），切到新文件继续读。
            if (e.Name?.EndsWith("_cclib.log", StringComparison.OrdinalIgnoreCase) == true)
                return;
            DiagLog.Write("CCM", $"FSW Created: {e.FullPath}");
            AppLog.Info(nameof(CcMiniTeammateMonitor), $"FSW Created: {e.FullPath}");
            lock (_stateLock)
            {
                _currentLogPath = e.FullPath;
                _lastPosition = 0;
            }
            ReadNewContent(e.FullPath);
        }

        private void OnLogChanged(object sender, FileSystemEventArgs e)
        {
            if (!_running) return;
            // 当前跟踪的文件被写入时才读增量；其它 m*.log（如旧会话）忽略。
            lock (_stateLock)
            {
                if (!string.Equals(_currentLogPath, e.FullPath, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            Task.Run(async () =>
            {
                try
                {
                    await _reader.TryReadWithLockAsync(
                        () => Task.Run(() => ReadNewContent(e.FullPath)),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(CcMiniTeammateMonitor)}.{nameof(OnLogChanged)}");
                }
            });
        }

        private async Task PollTickAsync(CancellationToken token)
        {
            lock (_stateLock)
            {
                if (!_running) return;
            }

            // 每轮重判活跃客户端目录：用户可能关掉 Steam 端、改开网易端（或反之）。
            // 两端不会同时登录，队友 UID 只会写进当前登录端的日志。这里检测到活跃目录
            // 切换时就重建 FileSystemWatcher + 重置读取位置，把监控切到另一端。
            var activeDir = ResolveCcMiniLogDir();
            bool dirSwitched;
            lock (_stateLock)
            {
                dirSwitched = activeDir != null &&
                              !string.Equals(_activeLogDir, activeDir, StringComparison.OrdinalIgnoreCase);
                if (dirSwitched)
                {
                    _activeLogDir = activeDir;
                    _currentLogPath = null;
                    _lastPosition = 0;
                }
            }
            if (dirSwitched)
            {
                AppLog.Info(nameof(CcMiniTeammateMonitor), $"活跃客户端日志目录切换: {activeDir}");
                RecreateWatcher(activeDir!);
                AttachToLatestLog(activeDir!);
            }

            // 客户端切换后刷新 player_prefs：用户从 Steam 切到网易（或反之），
            // PlayerId 可能已变，必须 reload 才能在 ExtractUids 中正确排除本地用户。
            if (dirSwitched)
            {
                try { await _playerPrefs.LoadAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    AppLog.Error(ex, nameof(CcMiniTeammateMonitor), "Poll tick reload prefs failed");
                }
            }

            // 兜底：每轮确认当前跟踪文件是否仍是最新 m*.log（新会话可能被 Created 事件漏触发）。
            var logDir = Path.GetDirectoryName(_currentLogPath);
            if (logDir != null)
                AttachToLatestLog(logDir);

            var path = _currentLogPath;
            if (path == null) return;
            await _reader.TryReadWithLockAsync(
                () => Task.Run(() => ReadNewContent(path)),
                token).ConfigureAwait(false);
        }

        /// <summary>
        /// 重建监听指定目录的 FileSystemWatcher（跨客户端切换时调用）。
        /// 若 watcher 已在监听同一目录则不动；否则停掉旧的、新建指向新目录的。
        /// </summary>
        private void RecreateWatcher(string logDir)
        {
            lock (_stateLock)
            {
                var oldPath = _watcher?.Path;
                if (!string.IsNullOrEmpty(oldPath) &&
                    string.Equals(oldPath, logDir, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnLogChanged;
                _watcher.Created -= OnLogCreated;
                _watcher.Dispose();
                _watcher = null;
            }

            _watcher = new FileSystemWatcher(logDir, "m*.log")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnLogChanged;
            _watcher.Created += OnLogCreated;
        }

        /// <summary>
        /// 增量读取当前文件新内容并解析 set-uid-vol 的 uid。每当集合内容变化（新队友加入、
        /// 队友退出换人）都会安排一次结算触发。文件不存在/读取失败一律静默跳过。
        /// </summary>
        private void ReadNewContent(string fullPath)
        {
            if (!File.Exists(fullPath)) return;

            long length;
            lock (_stateLock)
            {
                try { length = new FileInfo(fullPath).Length; }
                catch (Exception) { return; }
                if (length < _lastPosition)
                {
                    // 文件被截断/重建：从头读。
                    _lastPosition = 0;
                }
                if (length == _lastPosition) return;
            }

            var buffer = LogReader.ReadFileRangeAsync(fullPath, _lastPosition, length).GetAwaiter().GetResult();
            if (buffer == null || buffer.Length == 0) return;

            if (!LogReader.TruncateToLastNewline(buffer, out var text, out var consumedBytes)) return;

            List<string>? newUids = null;
            bool changed = false;
            lock (_stateLock)
            {
                _lastPosition += consumedBytes;
                newUids = ExtractUids(text);
                if (newUids.Count == 0)
                {
                    // 增量里没有新 UID（心跳/其它日志行），正常跳过；首轮可从日志中看到文件在增长。
                    return;
                }

                // 队友集合 = 最近活跃的 UID（LRU，容量 MaxTeammates）。
                // 新出现的 UID 视为"更活跃"，排到前面；同批已存在的 UID 也刷新到前端。
                var known = new HashSet<string>(_recognizedUids, StringComparer.OrdinalIgnoreCase);
                foreach (var uid in newUids)
                {
                    if (!known.Contains(uid))
                    {
                        known.Add(uid);
                        changed = true;
                    }
                }
                if (changed)
                {
                    // 重建顺序 = 本轮出现的 UID（保持出现先后）在前，其次旧集合中仍活跃的。
                    // 由于本轮 UID 是"刚写入"的，天然最新；GetRange(0, Max) 保留最新队友，旧队友被淘汰。
                    var rebuilt = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var uid in newUids)
                    {
                        if (seen.Add(uid)) rebuilt.Add(uid);
                    }
                    foreach (var uid in _recognizedUids)
                    {
                        if (seen.Add(uid)) rebuilt.Add(uid);
                    }
                    // 容量封顶：超出部分是"已退出"的旧队友，直接淘汰。
                    if (rebuilt.Count > MaxTeammates)
                        rebuilt = rebuilt.GetRange(0, MaxTeammates);

                    _recognizedUids.Clear();
                    _recognizedUids.AddRange(rebuilt);
                }

                if (!changed || _settleTaskStarted) return;
                _settleTaskStarted = true;
            }

            AppLog.Info(nameof(CcMiniTeammateMonitor),
                $"增量命中 set-uid-vol, 本轮新增={string.Join(",", newUids)}, 当前集合=[{string.Join(" | ", _recognizedUids)}]");
            _ = SettleAndFireAsync();
        }

        /// <summary>
        /// 结算窗口：等 <see cref="SettleDelay"/> 后按当前已收集的 UID 触发一次事件。
        /// 仅在集合签名（内容+顺序）与上次触发不同时才真正触发，避免重复刷同一批 UID。
        /// </summary>
        private async Task SettleAndFireAsync()
        {
            await Task.Delay(SettleDelay).ConfigureAwait(false);

            string signature;
            List<string> snapshot;
            lock (_stateLock)
            {
                _settleTaskStarted = false;
                if (_recognizedUids.Count == 0) return;

                signature = string.Join("\n", _recognizedUids);
                snapshot = _recognizedUids.ToList();
                if (string.Equals(signature, _lastFiredSignature, StringComparison.Ordinal))
                    return;
            }

            lock (_stateLock)
            {
                _lastFiredSignature = signature;
                _hasFiredOnce = true;
            }
            DiagLog.Write("CCM", $"触发队友名单 [{string.Join(" | ", snapshot)}]");
            AppLog.Info(nameof(CcMiniTeammateMonitor), $"触发队友名单 [{string.Join(" | ", snapshot)}]");
            TeammatesReady?.Invoke(this, new CcMiniTeammatesEventArgs
            {
                TeammateUids = snapshot
            });
        }

        /// <summary>
        /// 从文本中提取队伍语音频道（session-id=2）的 set-uid-vol 队友 UID。
        /// 只认队伍频道：该频道的 set-uid-vol 即队友组（每局 2 个=三排 / 1 个=双排）。
        /// 过滤掉本地用户与非法格式，按出现顺序去重后**逆序**返回（最新出现的在前）。
        /// </summary>
        private List<string> ExtractUids(string text)
        {
            var localUid = _playerPrefs.Current.PlayerId;
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in SetUidVolRegex.Matches(text))
            {
                var uid = m.Groups[1].Value;
                var sessionId = m.Groups[2].Value;
                // 只取队伍语音频道（session-id=2）。
                if (!string.Equals(sessionId, TeamSessionId, StringComparison.Ordinal)) continue;
                if (!UidPattern.IsMatch(uid)) continue;
                // 防御：排除本地用户 UID（set-uid-vol 正常只含队友，此分支几乎不触发）。
                if (!string.IsNullOrEmpty(localUid) &&
                    string.Equals(uid, localUid, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(uid)) result.Add(uid);
            }
            result.Reverse();
            return result;
        }
    }
}
