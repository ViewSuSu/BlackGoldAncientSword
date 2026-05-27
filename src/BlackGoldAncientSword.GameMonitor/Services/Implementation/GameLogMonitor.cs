using System.Text.RegularExpressions;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    using BlackGoldAncientSword.Framework.Core.Attributes;

    /// <summary>
    /// 游戏日志监视器。使用 .NET 原生异步 I/O 监听永劫无间 Player.log，
    /// 检测进入/离开对局事件并触发相应回调。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class GameLogMonitor : IGameLogMonitor
    {
        private static readonly string LogDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
            "24Entertainment", "Naraka");

        private static readonly string LogFileName = "Player.log";

        private static readonly Regex BattleTidRegex = new(
            @"battle_tid:(\d+)", RegexOptions.Compiled);

        private static readonly Regex MapIdRegex = new(
            @"map_id:\s*(\d+)", RegexOptions.Compiled);

        private static readonly Regex RoomIdRegex = new(
            @"roomid:([0-9a-fA-F]+)", RegexOptions.Compiled);

        private static readonly Regex RoomTypeRegex = new(
            @"room_type:(\d+)", RegexOptions.Compiled);

        private FileSystemWatcher? _watcher;
        private long _lastPosition;
        private string? _currentBattleId;
        private string? _currentMapId;
        private string? _currentRoomId;
        private string? _currentRoomType;
        private bool _isInBattle;
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _readSemaphore = new(1, 1);

        public event EventHandler<BattleEventArgs>? BattleStarted;
        public event EventHandler<BattleEventArgs>? BattleEnded;
        public event EventHandler<BattleEventArgs>? BattleJoined;

        public string? CurrentBattleId
        {
            get { lock (_stateLock) return _currentBattleId; }
        }

        public bool IsInBattle
        {
            get { lock (_stateLock) return _isInBattle; }
        }

        public bool IsRunning { get; private set; }

        /// <summary>
        /// 异步启动日志监视。使用 .NET 原生异步 I/O 读取已有日志内容。
        /// </summary>
        public async Task StartAsync()
        {
            if (IsRunning) return;

            var fullPath = System.IO.Path.Combine(LogDir, LogFileName);
            if (!System.IO.File.Exists(fullPath))
                return;

            await ReadExistingContentAsync(fullPath);
           
            _watcher = new FileSystemWatcher(LogDir, LogFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnLogChanged;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnLogChanged;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        public void Dispose()
        {
            Stop();
            _readSemaphore.Dispose();
        }

        /// <summary>
        /// 使用 .NET 原生异步 I/O 读取日志文件全部内容以同步状态。
        /// </summary>
        private async Task ReadExistingContentAsync(string fullPath)
        {
            lock (_stateLock)
            {
                try
                {
                    _lastPosition = new FileInfo(fullPath).Length;
                }
                catch { return; }
            }

            try
            {
                await using var fs = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    bufferSize: 4096, useAsync: true);
                using var reader = new StreamReader(fs);
                var content = await reader.ReadToEndAsync();
                ProcessContent(content);
            }
            catch { /* 忽略初次读取错误 */ }
        }

        /// <summary>
        /// 日志变更回调。用 SemaphoreSlim 串行化读取，防止并发事件导致读重叠。
        /// 通过 FileOptions.Asynchronous 启用真正的异步 I/O。
        /// </summary>
        private async void OnLogChanged(object sender, FileSystemEventArgs e)
        {
            // 串行化：一次只处理一个变更事件，后续事件排队等待
            if (!await _readSemaphore.WaitAsync(0))
                return; // 已有读操作在进行中，本次事件跳过（下次事件会读到累积内容）

            try
            {
                var fullPath = e.FullPath;
                long startPos;
                long endPos;

                // 在锁内捕获读取范围，然后释放锁再做异步 I/O
                lock (_stateLock)
                {
                    try
                    {
                        var fileInfo = new FileInfo(fullPath);
                        endPos = fileInfo.Length;
                        startPos = _lastPosition;

                        if (endPos < startPos)
                        {
                            // 日志被截断（游戏重启等），从头读取
                            startPos = 0;
                            endPos = fileInfo.Length;
                        }

                        _lastPosition = endPos;
                    }
                    catch { return; }
                }

                if (startPos >= endPos)
                    return;

                await using var fs = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    bufferSize: 4096, useAsync: true);
                fs.Seek(startPos, SeekOrigin.Begin);

                using var reader = new StreamReader(fs);
                var newContent = await reader.ReadToEndAsync();

                if (!string.IsNullOrEmpty(newContent))
                {
                    ProcessContent(newContent);
                }
            }
            catch { /* 忽略文件访问错误 */ }
            finally
            {
                _readSemaphore.Release();
            }
        }

        /// <summary>
        /// 解析日志内容（纯 CPU 操作，无需异步）。
        /// </summary>
        private void ProcessContent(string content)
        {
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                ProcessLine(line.TrimEnd('\r'));
            }
        }

        private void ProcessLine(string line)
        {
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

            if (line.Contains("OnMatchJoinTeam"))
            {
                var args = CreateCurrentBattleArgs();
                BattleJoined?.Invoke(this, args);
            }

            if (line.Contains("OnMatchEnter"))
            {
                lock (_stateLock) { _isInBattle = true; }

                if (!string.IsNullOrEmpty(CurrentBattleId))
                {
                    var args = CreateCurrentBattleArgs();
                    BattleStarted?.Invoke(this, args);
                }
            }

            if (line.Contains("TeamBattle Destroy"))
            {
                bool wasInBattle;
                lock (_stateLock)
                {
                    wasInBattle = _isInBattle;
                    _isInBattle = false;
                }

                if (wasInBattle)
                {
                    var args = CreateCurrentBattleArgs();
                    BattleEnded?.Invoke(this, args);

                    lock (_stateLock)
                    {
                        _currentBattleId = null;
                        _currentMapId = null;
                    }
                }
            }
        }

        private BattleEventArgs CreateCurrentBattleArgs()
        {
            lock (_stateLock)
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
        }
    }
}
