using System.Text;
using System.Text.RegularExpressions;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class GameLogMonitor : IGameLogMonitor
    {
        private static readonly Regex BattleTidRegex = new(
            @"battle_tid:(\d+)", RegexOptions.Compiled);

        private static readonly Regex MapIdRegex = new(
            @"map_id:\s*(\d+)", RegexOptions.Compiled);

        private static readonly Regex RoomIdRegex = new(
            @"roomid:([0-9a-fA-F]+)", RegexOptions.Compiled);

        private static readonly Regex RoomTypeRegex = new(
            @"room_type:(\d+)", RegexOptions.Compiled);

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private readonly ISettingsService _settings;

        private FileSystemWatcher? _watcher;
        private long _lastPosition;
        private string? _currentBattleId;
        private string? _currentMapId;
        private string? _currentRoomId;
        private string? _currentRoomType;
        private bool _isInBattle;
        private bool _joinedBattle;
        private bool _suppressEvents;
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _readSemaphore = new(1, 1);
        private CancellationTokenSource? _pollCts;

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

        public GameLogMonitor(ISettingsService settings)
        {
            _settings = settings;
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;

            var fullPath = _settings.Current.GameLogPath;
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
                return;

            await ReadExistingContentAsync(fullPath);

            var logDir = System.IO.Path.GetDirectoryName(fullPath) ?? ".";
            var logFile = System.IO.Path.GetFileName(fullPath);

            _watcher = new FileSystemWatcher(logDir, logFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnLogChanged;

            _pollCts = new CancellationTokenSource();
            _ = PollLoopAsync(fullPath, _pollCts.Token);

            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;

            if (_pollCts != null)
            {
                try { _pollCts.Cancel(); } catch { }
                _pollCts.Dispose();
                _pollCts = null;
            }

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

        private async Task PollLoopAsync(string fullPath, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PollInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!await _readSemaphore.WaitAsync(0, token))
                    continue;

                try
                {
                    await ReadNewContentAsync(fullPath);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
                finally
                {
                    try { _readSemaphore.Release(); } catch { }
                }
            }
        }

        private async Task ReadExistingContentAsync(string fullPath)
        {
            _suppressEvents = true;
            try
            {
                await using var fs = new System.IO.FileStream(
                    fullPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite,
                    bufferSize: 4096, useAsync: true);
                using var reader = new StreamReader(fs);
                var content = await reader.ReadToEndAsync();
                ProcessContent(content);

                lock (_stateLock)
                {
                    try { _lastPosition = new System.IO.FileInfo(fullPath).Length; }
                    catch { }
                }
            }
            catch { }
            finally
            {
                _suppressEvents = false;
                ResetState();
            }
        }

        private void OnLogChanged(object sender, System.IO.FileSystemEventArgs e)
        {
            Task.Run(async () =>
            {
                if (!await _readSemaphore.WaitAsync(0))
                    return;

                try
                {
                    await ReadNewContentAsync(e.FullPath);
                }
                catch
                {
                }
                finally
                {
                    _readSemaphore.Release();
                }
            });
        }

        private async Task ReadNewContentAsync(string fullPath)
        {
            long startPos;
            long endPos;

            lock (_stateLock)
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(fullPath);
                    endPos = fileInfo.Length;
                    startPos = _lastPosition;

                    if (endPos < startPos)
                    {
                        startPos = 0;
                        ResetState();
                    }
                }
                catch { return; }
            }

            if (startPos >= endPos)
                return;

            byte[]? buffer = await ReadFileRangeAsync(fullPath, startPos, endPos);
            if (buffer == null || buffer.Length == 0)
                return;

            int lastNewline = -1;
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    lastNewline = i;
                    break;
                }
            }

            if (lastNewline >= 0)
            {
                string completeContent = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
                ProcessContent(completeContent);

                lock (_stateLock)
                {
                    _lastPosition = startPos + lastNewline + 1;
                }
            }
        }

        private static async Task<byte[]?> ReadFileRangeAsync(string fullPath, long startPos, long endPos)
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    await using var fs = new System.IO.FileStream(
                        fullPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite,
                        bufferSize: 4096, useAsync: true);
                    fs.Seek(startPos, System.IO.SeekOrigin.Begin);

                    int bytesToRead = (int)(endPos - startPos);
                    var buffer = new byte[bytesToRead];
                    await fs.ReadExactlyAsync(buffer, 0, bytesToRead);
                    return buffer;
                }
                catch (System.IO.IOException)
                {
                    if (retry == 2) return null;
                    await Task.Delay(50);
                }
            }
            return null;
        }

        private void ResetState()
        {
            _isInBattle = false;
            _joinedBattle = false;
            _currentBattleId = null;
            _currentMapId = null;
            _currentRoomId = null;
            _currentRoomType = null;
            _lastPosition = 0;
        }

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

            if (line.Contains("开始连接战斗服务器"))
            {
                lock (_stateLock) { _joinedBattle = true; }
                if (!_suppressEvents)
                {
                    var args = CreateCurrentBattleArgs();
                    BattleJoined?.Invoke(this, args);
                }
            }

            if (_joinedBattle && (line.Contains("DoHideTeamOffLoadingPage") || line.Contains("TeamBattle Init")))
            {
                bool alreadyInBattle;
                lock (_stateLock)
                {
                    alreadyInBattle = _isInBattle;
                    if (!alreadyInBattle)
                    {
                        _isInBattle = true;
                        _joinedBattle = false;
                    }
                }

                if (!alreadyInBattle && !_suppressEvents)
                {
                    var args = CreateCurrentBattleArgs();
                    BattleStarted?.Invoke(this, args);
                }
            }

            if (line.Contains("TeamBattle Destroy") || line.Contains("GridMapManager Destroy"))
            {
                bool wasInBattle;
                lock (_stateLock)
                {
                    wasInBattle = _isInBattle;
                    _isInBattle = false;
                    _joinedBattle = false;
                }

                if (wasInBattle && !_suppressEvents)
                {
                    if (!_suppressEvents)
                    {
                        var args = CreateCurrentBattleArgs();
                        BattleEnded?.Invoke(this, args);
                    }

                    lock (_stateLock)
                    {
                        _currentBattleId = null;
                        _currentMapId = null;
                    }
                }
            }

            if (line.Contains("NetAgent DisconnectFromEnet"))
            {
                bool joined, inBattle;
                lock (_stateLock)
                {
                    joined = _joinedBattle;
                    inBattle = _isInBattle;
                }

                if (joined && !inBattle)
                {
                    lock (_stateLock) { _joinedBattle = false; }
                    if (!_suppressEvents)
                    {
                        var args = CreateCurrentBattleArgs();
                        BattleEnded?.Invoke(this, args);
                    }

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