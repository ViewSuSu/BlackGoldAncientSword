using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    /// <summary>
    /// 游戏日志监视器 facade。本身不再承担文件读取 / Poll 循环 / 状态机这三件事——
    /// 分别委托给 <see cref="LogReader"/> / <see cref="LogPoller"/> / <see cref="BattleStateMachine"/>。
    /// 自己只负责：FileSystemWatcher 生命周期、对外事件分发、Stop/Dispose 顺序、IsRunning 早退。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class GameLogMonitor : IGameLogMonitor
    {
        private readonly LogReader _reader = new();
        private readonly LogPoller _poller = new();
        private readonly BattleStateMachine _stateMachine = new();

        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _pollCts;
        // 保留 PollLoop 的 Task 句柄：Stop/Dispose 必须 await 它退出，
        // 否则 Dispose 释放 _reader 后 PollLoop 仍在跑会触发 ObjectDisposedException。
        private Task? _pollTask;

        public event EventHandler<BattleEventArgs>? BattleStarted;
        public event EventHandler<BattleEventArgs>? BattleEnded;
        public event EventHandler<BattleEventArgs>? BattleJoined;

        public string? CurrentBattleId => _stateMachine.CurrentBattleId;
        public bool IsInBattle => _stateMachine.IsInBattle;
        public bool IsRunning { get; private set; }

        public GameLogMonitor()
        {
        }

        public async Task StartAsync()
        {
            DiagLog.Write("GLM", "StartAsync 入口, IsRunning=" + IsRunning);
            if (IsRunning) return;

            var fullPath = Framework.Services.AppSettings.GetDefaultGameLogPath();
            DiagLog.Write("GLM", $"日志路径={fullPath}, Exists={File.Exists(fullPath)}");
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                return;

            await ReplayExistingContentAsync(fullPath).ConfigureAwait(false);
            DiagLog.Write("GLM", $"Replay 结束, isInBattle={_stateMachine.IsInBattle}, isJoined={_stateMachine.IsJoined}, battleId={_stateMachine.CurrentBattleId}, lastPos={_stateMachine.LastPosition}");

            // Replay 完毕后按当前状态机内容补发一次事件，让已订阅的 UI 反映现网对局阶段
            // （冷启动进入正在进行的对局时不再显示空）。
            PublishSnapshot();

            var logDir = Path.GetDirectoryName(fullPath) ?? ".";
            var logFile = Path.GetFileName(fullPath);

            _watcher = new FileSystemWatcher(logDir, logFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnLogChanged;

            _pollCts = new CancellationTokenSource();
            _pollTask = _poller.RunAsync(
                token => _reader.TryReadWithLockAsync(() => ReadNewContentAsync(fullPath), token),
                _pollCts.Token);

            IsRunning = true;
            DiagLog.Write("GLM", "StartAsync 完成, FSW+Poll 已启动");
        }

        public void Stop()
        {
            // 顺序：1) 置 IsRunning=false（OnLogChanged 早退）；2) 取消 PollLoop 的 token；
            // 3) FSW 停 raise + 解绑 + Dispose；4) 等 _pollTask 退出；5) Dispose CTS（仅在按时退出时）。
            // 故意先 Cancel 再 Dispose FSW：先打断异步路径，避免 _watcher.Dispose 期间 PollLoop 仍在
            // 持有 token 跑读取动作。
            // **故意不 Dispose _reader（其内部 semaphore）** —— 后续可能仍有 in-flight OnLogChanged
            // 在 Task.Run 队列中，semaphore 必须保持可用；真正释放放到 Dispose() 里，
            // 并依赖 LogReader / OnLogChanged 内部 catch ObjectDisposedException 兜底剩余的 race 窗口。
            IsRunning = false;

            if (_pollCts != null)
            {
                try { _pollCts.Cancel(); }
                catch (Exception ex)
                {
                    // 极端情况下 _pollCts 已被另一路径 Dispose；吞掉以保证 Stop 不抛，但留诊断。
                    Debug.WriteLine($"[{nameof(GameLogMonitor)}] _pollCts.Cancel failed: {ex.Message}");
                }
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnLogChanged;
                _watcher.Dispose();
                _watcher = null;
            }

            if (_pollCts != null)
            {
                // 等待 PollLoop 真正退出再释放 CTS / semaphore；超时 500ms 防止 Stop 卡死。
                // Stop 是同步签名，无法 await——这里用同步等待是有意的权衡（非 IO 路径）。
                if (_pollTask != null)
                {
                    bool exited = false;
                    try { exited = _pollTask.Wait(TimeSpan.FromMilliseconds(500)); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[{nameof(GameLogMonitor)}] _pollTask wait failed: {ex.Message}");
                    }

                    if (!exited)
                    {
                        // PollLoop 未在 500ms 内退出。若此刻 Dispose CTS，PollLoop 仍持有 token 触发的
                        // 注册回调路径会抛 ObjectDisposedException（LogReader 已吞 ODE，但 token.Register
                        // 等内部路径可能未覆盖）。宁可接受小泄漏 (一个 CTS+Token 句柄)，也不冒崩溃风险。
                        Debug.WriteLine($"[{nameof(GameLogMonitor)}] PollLoop 未在 500ms 内退出，保留 CTS 避免 ODE");
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

        public void PublishSnapshot()
        {
            if (_stateMachine.IsInBattle)
            {
                DiagLog.Write("GLM", "PublishSnapshot -> BattleStarted");
                BattleStarted?.Invoke(this, _stateMachine.CurrentSnapshot);
            }
            else if (_stateMachine.IsJoined)
            {
                DiagLog.Write("GLM", "PublishSnapshot -> BattleJoined");
                BattleJoined?.Invoke(this, _stateMachine.CurrentSnapshot);
            }
            else
            {
                DiagLog.Write("GLM", "PublishSnapshot -> 无事件 (非对局中)");
            }
        }

        public void Dispose()
        {
            Stop();
            // Dispose 在 Stop 之后，但 OnLogChanged 已被解绑、IsRunning=false 早退；
            // 即便残余 in-flight 仍能命中 ObjectDisposedException catch 而非崩溃 ThreadPool。
            _reader.Dispose();
        }

        /// <summary>
        /// 启动期回放：把现有日志整文件读一遍以建立状态机当前对局状态，但抑制事件触发。
        /// 失败不抛——监控功能不可用比让 StartAsync 抛更可接受（上层 SafeFireAndForget 只 log）。
        /// </summary>
        private async Task ReplayExistingContentAsync(string fullPath)
        {
            _stateMachine.BeginSuppressedReplay();
            try
            {
                var (content, length) = await _reader.ReadAllAsync(fullPath).ConfigureAwait(false);
                // 抑制期 ProcessContent 不会返回事件，无需触发。
                _stateMachine.ProcessContent(content);
                _stateMachine.SetLastPosition(length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(GameLogMonitor)}] ReplayExistingContent failed: {ex.Message}");
            }
            finally
            {
                _stateMachine.EndSuppressedReplay();
            }
        }

        private void OnLogChanged(object sender, FileSystemEventArgs e)
        {
            // FileSystemWatcher 在 ThreadPool raise；Stop()/Dispose() 之后 FSW 内部仍可能 schedule
            // 一到两次回调（已经入队但未跑）。这些回调若进到下面对已 Dispose 的 _reader.semaphore
            // 做 WaitAsync/Release，会在线程池上裸抛 ObjectDisposedException → 进程崩溃。
            // 此处用 IsRunning 早退；LogReader 内部再吞 ObjectDisposedException 双保险。
            if (!IsRunning) return;

            Task.Run(async () =>
            {
                try
                {
                    await _reader.TryReadWithLockAsync(
                        () => ReadNewContentAsync(e.FullPath),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // 其它异常吞掉以免崩溃 ThreadPool；监控失效本身不应让进程挂掉。
                    // 排除 OOM / SOF 这两个进程级致命异常——它们必须传播出去。
                    Debug.WriteLine($"[{nameof(GameLogMonitor)}.{nameof(OnLogChanged)}] {ex}");
                }
            });
        }

        /// <summary>
        /// 计算增量字节范围 → 读取 → 截到最后一个完整行 → 喂给状态机 → 分发事件 → 提交位置。
        /// </summary>
        private async Task ReadNewContentAsync(string fullPath)
        {
            var length = LogReader.TryGetFileLength(fullPath);
            if (length == null) return;

            var range = _stateMachine.PrepareReadRange(length.Value);
            if (range == null) return;

            var (startPos, endPos) = range.Value;
            if (startPos >= endPos) return;

            byte[]? buffer = await LogReader.ReadFileRangeAsync(fullPath, startPos, endPos).ConfigureAwait(false);
            if (buffer == null || buffer.Length == 0) return;

            // 截断到最后一个完整行 + UTF-8 解码下沉到 LogReader，facade 只负责装配与事件分发。
            if (!LogReader.TruncateToLastNewline(buffer, out var completeContent, out var consumedBytes)) return;

            var events = _stateMachine.ProcessContent(completeContent);
            _stateMachine.CommitReadPosition(startPos + consumedBytes);

            if (events.Count > 0)
                DiagLog.Write("GLM", $"ReadNewContent: {events.Count} 事件, range=[{startPos},{endPos}), consumed={consumedBytes}");
            foreach (var (kind, args) in events)
            {
                DiagLog.Write("GLM", $"emit {kind}, battleId={args.BattleId}, mapId={args.MapId}");
                switch (kind)
                {
                    case BattleEventKind.Joined:
                        BattleJoined?.Invoke(this, args);
                        break;
                    case BattleEventKind.Started:
                        BattleStarted?.Invoke(this, args);
                        break;
                    case BattleEventKind.Ended:
                        BattleEnded?.Invoke(this, args);
                        break;
                }
            }
        }
    }
}
