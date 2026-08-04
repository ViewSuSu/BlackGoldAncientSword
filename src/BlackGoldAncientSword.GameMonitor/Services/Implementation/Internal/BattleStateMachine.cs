using System.Text.RegularExpressions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

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

        public bool IsJoined
        {
            get { lock (_stateLock) return _joinedBattle; }
        }

        /// <summary>
        /// 抓当前 battle/map/room 快照，供 GameLogMonitor 在启动期回放结束后补发一次事件反映现网状态。
        /// </summary>
        public BattleEventArgs CurrentSnapshot => CreateCurrentBattleArgs();

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
        /// 启动期回放结束：解除事件抑制，保留 replay 结束时的战斗状态（in-battle / joined / battleId 等），
        /// 让上层 GameLogMonitor 能据此补发一次"现网快照"事件，令 UI 反映当前对局阶段。
        /// 若在这里清 state，冷启动进入正在进行的对局时 UI 将永远是空/Unknown。
        /// </summary>
        public void EndSuppressedReplay()
        {
            lock (_stateLock)
            {
                _suppressEvents = false;
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
            // —— 1) ID 提取 ——
            // battle_tid 变化处理：若旧 id 非空且与新 id 不同，且 _isInBattle/_joinedBattle 仍为 true，
            // 说明上一局没有触发正常 Ended（游戏 crash / 玩家杀进程 / 非常规退出对局，无 TeamBattle Destroy
            // / GridMapManager Destroy 日志），残留状态会让"开始连接战斗服务器"分支中的
            // alreadyInBattle 保护把新一局的 Joined 事件全部吞掉 → OCR 循环永远不启动。
            // 因此在检测到新 battle_tid 时强制视为老局隐式 Ended，reset in-battle/joined，
            // 并 emit 一个 Ended 事件让上层可以正确清理 UI。
            // 真正的 mid-battle 掉线重连场景 battle_tid 保持不变，走 else 分支不受影响。
            var battleTidMatch = BattleTidRegex.Match(line);
            BattleEventArgs? implicitEndedArgs = null;
            if (battleTidMatch.Success)
            {
                var newBattleId = battleTidMatch.Groups[1].Value;
                lock (_stateLock)
                {
                    var oldBattleId = _currentBattleId;
                    _currentBattleId = newBattleId;

                    if (!string.IsNullOrEmpty(oldBattleId)
                        && !string.Equals(oldBattleId, newBattleId, StringComparison.Ordinal)
                        && (_isInBattle || _joinedBattle))
                    {
                        if (!_suppressEvents)
                        {
                            implicitEndedArgs = new BattleEventArgs
                            {
                                BattleId = oldBattleId,
                                MapId = _currentMapId ?? string.Empty,
                                RoomId = _currentRoomId ?? string.Empty,
                                RoomType = _currentRoomType ?? string.Empty,
                                Timestamp = DateTimeOffset.Now
                            };
                        }
                        _isInBattle = false;
                        _joinedBattle = false;
                        _currentMapId = null;
                    }
                }
            }
            if (implicitEndedArgs != null)
            {
                // 老局隐式结束事件先行返回，本行剩余的 Joined/Started/Ended 判断留给状态机的下一行处理
                // （battle_tid 通常独占一行输出，不会与 "开始连接战斗服务器" 等关键 marker 同行）。
                return (BattleEventKind.Ended, implicitEndedArgs);
            }

            var mapIdMatch = MapIdRegex.Match(line);
            if (mapIdMatch.Success)
            {
                // map_id 只用于记录当前战斗场景 id（供 Ended 事件 args 使用），不作为 Started 触发信号。
                // 曾经把"已 Joined 且首次拿到 map_id"当作进对局的补充 Started 信号，但实测发现：
                // 永劫在"开始连接战斗服务器"（=进入英雄选择）后同一秒就写入 map_id（StartLoadBattleScenePacket），
                // 而真正进入对局（英雄选择结束）是几十秒后的 DoHideTeamOffLoadingPage。
                // 用 map_id 触发 Started 会让状态在 Joined 后几毫秒即翻成 InGame，英雄选择窗口坍缩，
                // 队伍 OCR 识别循环启动即被 StopOcrLoop 取消 → 整局英雄选择阶段都不识别队友。
                // Started 只由下方 DoHideTeamOffLoadingPage / TeamBattle Init 触发。
                lock (_stateLock)
                {
                    _currentMapId = mapIdMatch.Groups[1].Value;
                }
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
            // 若 _isInBattle=true，说明是 mid-battle 掉线重连（game 会再次写这一行，battle_tid 通常不变），
            // 此时对局仍在进行，不应触发 Joined 事件把 UI 打回 HeroSelection。
            if (line.Contains("开始连接战斗服务器"))
            {
                bool suppressed;
                bool alreadyInBattle;
                lock (_stateLock)
                {
                    alreadyInBattle = _isInBattle;
                    if (!alreadyInBattle)
                    {
                        _joinedBattle = true;
                    }
                    suppressed = _suppressEvents;
                }
                DiagLog.Write("BSM", $"命中 '开始连接战斗服务器', alreadyInBattle={alreadyInBattle}, suppressed={suppressed}, battleId={_currentBattleId}");
                if (!alreadyInBattle && !suppressed)
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

        /// <summary>
        /// 仅复位战斗相关状态（in-battle / joined / 各类 id），保留 <see cref="LastPosition"/>。
        /// 用于启动期回放结束后、发现游戏进程未运行时清理残留：历史日志最后一场对局若因玩家杀进程 /
        /// 关游戏而缺失 Destroy marker，会让状态机残留 InBattle=true → PublishSnapshot 误触发
        /// BattleStarted。此处清 battle 状态但保留读取偏移，避免 FSW 增量重扫全文。
        /// </summary>
        public void ResetBattleState()
        {
            lock (_stateLock)
            {
                _isInBattle = false;
                _joinedBattle = false;
                _currentBattleId = null;
                _currentMapId = null;
                _currentRoomId = null;
                _currentRoomType = null;
            }
        }
    }
}
