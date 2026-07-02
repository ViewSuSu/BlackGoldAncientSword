using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class SettingsService : ISettingsService, IDisposable
    {
        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>SaveAsync 完成到 FileSystemWatcher 回响之间的抑制窗口。写盘 -> OS 通知 -> 事件回调
        /// 之间存在数十到数百毫秒的延迟，取 800ms 覆盖大多数情况；短于 debounce 也无所谓，回响会被 Reload
        /// 后的哈希比对拦截。</summary>
        private const int SelfWriteEchoWindowMs = 800;

        /// <summary>Watcher 事件到 Reload 之间的合并窗口。编辑器保存往往触发多次 Changed（写入 + 属性），
        /// debounce 到最后一次事件，减少无用 Reload。</summary>
        private const int WatcherDebounceMs = 300;

        public AppSettings Current { get; private set; } = new();

        public event EventHandler? SettingsChanged;

        private string FilePath => Path.Combine(
            AppSettings.GetDefaultPath(), "settings.json");

        private Task? _loadTask;
        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private readonly object _watcherLock = new();
        private DateTime _lastSelfWriteUtc = DateTime.MinValue;
        private string? _lastKnownJson;
        private bool _disposed;

        public SettingsService()
        {
            // 构造时触发异步加载，LoadAsync 返回的 Task 可被外部 await 等待完成
            LoadAsync().SafeFireAndForget($"{nameof(SettingsService)}.{nameof(LoadAsync)}");
            InitWatcher();
        }

        /// <summary>
        /// 强制重新从 settings.json 加载配置，不做缓存，覆盖 Current。
        /// </summary>
        public async Task ReloadAsync()
        {
            _loadTask = null;
            await LoadAsync();
        }

        /// <summary>
        /// 异步从 settings.json 加载配置。可多次调用，内部缓存 Task 避免重复加载。
        /// </summary>
        public Task LoadAsync()
        {
            if (_loadTask != null)
                return _loadTask;
            _loadTask = LoadInternalAsync();
            return _loadTask;
        }

        private async Task LoadInternalAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 确保缓存目录存在
                var cachePath = AppSettings.GetDefaultCachePath();
                if (!Directory.Exists(cachePath))
                    Directory.CreateDirectory(cachePath);

                if (File.Exists(FilePath))
                {
                    var json = await File.ReadAllTextAsync(FilePath);
                    Current = JsonSerializer.Deserialize<AppSettings>(json, _readOptions) ?? new AppSettings();
                    _lastKnownJson = json;
                }
                else
                {
                    Current = new AppSettings
                    {
                        DataSavePath = AppSettings.GetDefaultPath(),
                        CachePath = AppSettings.GetDefaultCachePath(),
                        Language = "zh-CN"
                    };
                    _lastKnownJson = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(SettingsService)}] {nameof(LoadAsync)} failed: {ex.Message}");
                Current = new AppSettings();
            }
        }

        /// <summary>
        /// 异步保存配置到 settings.json。保存完成后广播 <see cref="SettingsChanged"/>，
        /// 供订阅方（例如设置页 ViewModel）即时刷新 UI。
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(Current, _writeOptions);
                // 写盘前后各标记一次时间戳：
                // - 写盘前设置，覆盖极端场景下 OS 事件先于 await 返回的情况；
                // - 写盘完成后再次刷新，确保回响窗口从"真实落盘时刻"起算。
                _lastSelfWriteUtc = DateTime.UtcNow;
                await File.WriteAllTextAsync(FilePath, json);
                _lastSelfWriteUtc = DateTime.UtcNow;
                _lastKnownJson = json;

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(SettingsService)}] {nameof(SaveAsync)} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化 FileSystemWatcher 监听 settings.json 所在目录。Watcher 内部使用系统线程池
        /// 通知，无需自建线程；事件回调将在非 UI 线程触发，订阅方需自行 marshal。
        /// </summary>
        private void InitWatcher()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (string.IsNullOrEmpty(dir))
                    return;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _watcher = new FileSystemWatcher(dir, "settings.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite
                                 | NotifyFilters.Size
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false,
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;
                _watcher.Error += OnWatcherError;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(SettingsService)}] {nameof(InitWatcher)} failed: {ex.Message}");
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e) => ScheduleReload();

        private void OnFileChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Debug.WriteLine($"[{nameof(SettingsService)}] Watcher error: {e.GetException()?.Message}");
            // 尝试重建。缓冲区溢出等错误会禁用 watcher，需重建才能继续监听。
            try
            {
                _watcher?.Dispose();
                _watcher = null;
                InitWatcher();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(SettingsService)}] Watcher rebuild failed: {ex.Message}");
            }
        }

        private void ScheduleReload()
        {
            // 抑制自写入回响：SaveAsync 完成后短时间内触发的 watcher 事件视为自己写盘的回声，直接丢弃。
            if ((DateTime.UtcNow - _lastSelfWriteUtc).TotalMilliseconds < SelfWriteEchoWindowMs)
                return;

            lock (_watcherLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ => ReloadFromWatcher().SafeFireAndForget(
                    $"{nameof(SettingsService)}.{nameof(ReloadFromWatcher)}"),
                    null, WatcherDebounceMs, Timeout.Infinite);
            }
        }

        private async Task ReloadFromWatcher()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                // 读原始 json 与上次已知内容比对，避免"文件被 touch 但内容未变"触发无谓广播。
                string json;
                try
                {
                    json = await File.ReadAllTextAsync(FilePath);
                }
                catch (IOException)
                {
                    // 写入方尚未释放句柄，稍后重试一次
                    await Task.Delay(150);
                    json = await File.ReadAllTextAsync(FilePath);
                }

                if (string.Equals(json, _lastKnownJson, StringComparison.Ordinal))
                    return;

                var parsed = JsonSerializer.Deserialize<AppSettings>(json, _readOptions);
                if (parsed == null)
                    return;

                Current = parsed;
                _lastKnownJson = json;

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(SettingsService)}] {nameof(ReloadFromWatcher)} failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Changed -= OnFileChanged;
                    _watcher.Created -= OnFileChanged;
                    _watcher.Renamed -= OnFileRenamed;
                    _watcher.Error -= OnWatcherError;
                    _watcher.Dispose();
                    _watcher = null;
                }
                lock (_watcherLock)
                {
                    _debounceTimer?.Dispose();
                    _debounceTimer = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(SettingsService)}] {nameof(Dispose)} failed: {ex.Message}");
            }
            GC.SuppressFinalize(this);
        }
    }
}
