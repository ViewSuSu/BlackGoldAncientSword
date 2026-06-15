using System.Text.RegularExpressions;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal
{
    /// <summary>
    /// 触发的战斗事件类型。<see cref="None"/> 表示该行未引发状态切换。
    /// </summary>
    internal enum BattleEventKind
    {
        None,
        Joined,
        Started,
        Ended
    }

    /// <summary>
    /// 战斗状态机：消费日志行 → 提取 battle/map/room id → 推进 joined / inBattle 状态机 → 输出事件。
    /// 自包含状态：所有字段在 <see cref="_stateLock"/> 下读写；同时承载文件读取位置 <see cref="LastPosition"/>，
    /// 因为它的复位（truncate / 启动期回放结束）必须与战斗状态原子化。
    /// </summary>
    internal sealed class BattleStateMachine
    {
        private static readonly Regex BattleTidRegex = new(
            @"battle_tid:(\d+)", RegexOptions.Compiled);

        private static readonly Regex MapIdRegex = new(
            @"map_id:\s*(\d+)", RegexOptions.Compiled);

        private static readonly Regex RoomIdRegex = new(
            @"roomid:([0-9a-fA-F]+)", RegexOptions.Compiled);

        private static readonly Regex RoomTypeRegex = new(
            @"room_type:(\d+)", RegexOptions.Compiled);

        private readonly object _stateLock = new();

        private string? _currentBattleId;
        private string? _currentMapId;
        private string? _currentRoomId;
        private string? _currentRoomType;
        private bool _isInBattle;
        private bool _joinedBattle;
        private bool _suppressEvents;
        private long _lastPosition;

        public string? CurrentBattleId
        {
            get { lock (_stateLock) return _currentBattleId; }
        }

        public bool IsInBattle
        {
            get { lock (_stateLock) return _isInBattle; }
        }

        /// <summary>
        /// 当前已消费日志的字节偏移。FSW/Poll 在读取增量前后须同步此字段。
        /// </summary>
        public long LastPosition
        {
            get { lock (_stateLock) return _lastPosition; }
        }

        /// <summary>
        /// 启动期回放历史日志时调用：抑制事件触发以避免上层把"陈年战斗"当成现网事件处理。
        /// </summary>
        public void BeginSuppressedReplay()
        {
            lock (_stateLock) { _suppressEvents = true; }
        }

        /// <summary>
        /// 启动期回放结束：解除抑制并重置状态机（清空 battleId/inBattle 等），
        /// 让真正的现网增量从干净状态开始。两步必须在同一锁内原子完成，
        /// 否则在 _suppressEvents=false 与 ResetLocked() 之间到达的 ProcessLine 可能
        /// 在脏状态下触发事件。
        /// </summary>
        public void EndSuppressedReplay()
        {
            lock (_stateLock)
            {
                _suppressEvents = false;
                ResetLocked();
            }
        }

        /// <summary>
        /// 启动期一次性读取后，把回放完毕的字节长度写入 LastPosition。
        /// </summary>
        public void SetLastPosition(long position)
        {
            lock (_stateLock) { _lastPosition = position; }
        }

        /// <summary>
        /// 计算本轮增量读取的字节范围。若检测到文件被截断（endPos &lt; LastPosition），
        /// 在 lock 内 reset 状态机并将 startPos 回到 0，再返回新范围。返回 null 表示读取失败应跳过。
        /// </summary>
        public (long startPos, long endPos)? PrepareReadRange(long currentLength)
        {
            lock (_stateLock)
            {
                long endPos = currentLength;
                long startPos = _lastPosition;

                if (endPos < startPos)
                {
                    // 文件被截断/重建：重置状态机并从头读。
                    startPos = 0;
                    ResetLocked();
                }

                return (startPos, endPos);
            }
        }

        /// <summary>
        /// 提交本轮成功消费的字节末偏移。
        /// </summary>
        public void CommitReadPosition(long newPosition)
        {
            lock (_stateLock) { _lastPosition = newPosition; }
        }

        /// <summary>
        /// 处理一段日志内容（可能包含多行）。返回本段触发的事件序列（按行序）。
        /// </summary>
        public IReadOnlyList<(BattleEventKind kind, BattleEventArgs args)> ProcessContent(string content)
        {
            var events = new List<(BattleEventKind, BattleEventArgs)>();
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var ev = ProcessLine(line.TrimEnd('\r'));
                if (ev.kind != BattleEventKind.None)
                    events.Add(ev);
            }
            return events;
        }

        /// <summary>
        /// 处理一行日志。返回触发的事件（<see cref="BattleEventKind.None"/> 表示无）。
        /// 抑制期（启动期回放）始终返回 None，但仍会更新内部 id / 状态字段。
        /// </summary>
        public (BattleEventKind kind, BattleEventArgs args) ProcessLine(string line)
        {
            // —— 1) ID 提取（无事件副作用）——
            var battleTidMatch = BattleTidRegex.Match(line);
            if (battleTidMatch.Success)
            {
                lock (_stateLock) { _currentBattleId = battleTidMatch.Groups[1].Value; }
            }

            var mapIdMatch = MapIdRegex.Match(line);
            if (mapIdMatch.Success)
            {
                lock (_stateLock) { _currentMapId = mapIdMatch.Groups[1].Value; }
            }

            var roomIdMatch = RoomIdRegex.Match(line);
            if (roomIdMatch.Success)
            {
                lock (_stateLock) { _currentRoomId = roomIdMatch.Groups[1].Value; }
            }

            var roomTypeMatch = RoomTypeRegex.Match(line);
            if (roomTypeMatch.Success)
            {
                lock (_stateLock) { _currentRoomType = roomTypeMatch.Groups[1].Value; }
            }

            // —— 2) Joined：开始连接战斗服务器 ——
            if (line.Contains("开始连接战斗服务器"))
            {
                bool suppressed;
                lock (_stateLock)
                {
                    _joinedBattle = true;
                    suppressed = _suppressEvents;
                }
                if (!suppressed)
                {
                    return (BattleEventKind.Joined, CreateCurrentBattleArgs());
                }
            }

            // —— 3) Started：joined 后看到进入对局标志 ——
            // 注意：_joinedBattle 读取也应在锁内，否则可能读到过期值；这里保留原语义只在条件成立后入锁复查。
            bool joinedSnapshot;
            lock (_stateLock) { joinedSnapshot = _joinedBattle; }
            if (joinedSnapshot && (line.Contains("DoHideTeamOffLoadingPage") || line.Contains("TeamBattle Init")))
            {
                bool alreadyInBattle;
                bool suppressed;
                lock (_stateLock)
                {
                    alreadyInBattle = _isInBattle;
                    if (!alreadyInBattle)
                    {
                        _isInBattle = true;
                        _joinedBattle = false;
                    }
                    suppressed = _suppressEvents;
                }

                if (!alreadyInBattle && !suppressed)
                {
                    return (BattleEventKind.Started, CreateCurrentBattleArgs());
                }
            }

            // —— 4) Ended：对局正常结束 ——
            // build args + clear battle/map id 在同一锁内完成，避免跨锁时序窗口；
            // CreateCurrentBattleArgsLocked 是 lock-free 版本，调用方已持锁。
            if (line.Contains("TeamBattle Destroy") || line.Contains("GridMapManager Destroy"))
            {
                BattleEventArgs? endedArgs = null;
                lock (_stateLock)
                {
                    bool wasInBattle = _isInBattle;
                    _isInBattle = false;
                    _joinedBattle = false;
                    if (wasInBattle && !_suppressEvents)
                    {
                        endedArgs = CreateCurrentBattleArgsLocked();
                        _currentBattleId = null;
                        _currentMapId = null;
                    }
                }

                if (endedArgs != null)
                {
                    return (BattleEventKind.Ended, endedArgs);
                }
            }

            // —— 5) Ended（异常）：joined 后未真正开始即断线 ——
            // 全部判定 + args 构造 + clear id 合并到同一锁内，杜绝跨锁条件竞争。
            if (line.Contains("NetAgent DisconnectFromEnet"))
            {
                BattleEventArgs? endedArgs = null;
                lock (_stateLock)
                {
                    if (_joinedBattle && !_isInBattle)
                    {
                        _joinedBattle = false;
                        if (!_suppressEvents)
                        {
                            endedArgs = CreateCurrentBattleArgsLocked();
                            _currentBattleId = null;
                            _currentMapId = null;
                        }
                    }
                }

                if (endedArgs != null)
                {
                    return (BattleEventKind.Ended, endedArgs);
                }
            }

            return (BattleEventKind.None, default!);
        }

        private BattleEventArgs CreateCurrentBattleArgs()
        {
            lock (_stateLock)
            {
                return CreateCurrentBattleArgsLocked();
            }
        }

        /// <summary>
        /// CreateCurrentBattleArgs 的 lock-free 版本：调用方必须已持有 <see cref="_stateLock"/>。
        /// 抽出该方法是为了让 ProcessLine 的 Ended 分支能在单锁内完成 "build args + clear id"。
        /// </summary>
        private BattleEventArgs CreateCurrentBattleArgsLocked()
        {
            return new BattleEventArgs
            {
                BattleId = _currentBattleId ?? string.Empty,
                MapId = _currentMapId ?? string.Empty,
                RoomId = _currentRoomId ?? string.Empty,
                RoomType = _currentRoomType ?? string.Empty,
                Timestamp = DateTimeOffset.Now
            };
        }

        private void ResetLocked()
        {
            _isInBattle = false;
            _joinedBattle = false;
            _currentBattleId = null;
            _currentMapId = null;
            _currentRoomId = null;
            _currentRoomType = null;
            _lastPosition = 0;
        }
    }
}
