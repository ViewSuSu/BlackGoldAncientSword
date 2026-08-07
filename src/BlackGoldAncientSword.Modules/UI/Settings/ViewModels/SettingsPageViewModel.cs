using System.Collections.ObjectModel;
using Microsoft.Win32;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using System.Diagnostics;

namespace BlackGoldAncientSword.Modules.UI.Settings.ViewModels
{
    public class SettingsPageViewModel : ViewModelBase
    {
        private string L(string key, string fallback) => _localizedText.Get(key, fallback);
        private readonly ISettingsService _settings;
        private readonly ILocalizationService _localization;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly IMainContentNavigationService _navigation;
        private readonly IImageCacheService _cacheService;
        private readonly IUpdateService _updateService;
        private readonly IClipboardService _clipboard;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly IUiScaleService _uiScale;

        /// <summary>立即异步落盘。设置项变更点必须实时持久化，避免"改完立即关闭/Kill 进程"
        /// 导致丢失。所有设置项均为单次用户动作（点击单选/勾选/选完文件夹），不存在高频
        /// 调用场景，无需防抖。</summary>
        private void SaveImmediate([System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
        {
            _settings.SaveAsync().SafeFireAndForget($"{nameof(SettingsPageViewModel)}.{caller}.SaveAsync");
        }

        private string _dataPath = string.Empty;
        public string DataPath
        {
            get => _dataPath;
            set
            {
                if (_dataPath == value) return;
                _dataPath = value;
                RaisePropertyChanged(nameof(DataPath));

                if (!string.IsNullOrWhiteSpace(value) && !System.IO.Directory.Exists(value))
                    return;

                _settings.Current.DataSavePath = value;
                SaveImmediate();
            }
        }

        private string _cachePath = string.Empty;
        public string CachePath
        {
            get => _cachePath;
            set
            {
                if (_cachePath == value) return;
                _cachePath = value;
                RaisePropertyChanged(nameof(CachePath));

                if (!string.IsNullOrWhiteSpace(value) && !System.IO.Directory.Exists(value))
                    return;

                _settings.Current.CachePath = value;
                _cacheService.CachePath = value;
                SaveImmediate();
            }
        }

        private string _logPath = string.Empty;
        public string LogPath
        {
            get => _logPath;
            set
            {
                if (_logPath == value) return;
                _logPath = value;
                RaisePropertyChanged(nameof(LogPath));

                if (!string.IsNullOrWhiteSpace(value) && !System.IO.Directory.Exists(value))
                    return;

                _settings.Current.LogPath = value;
                // 立即让日志切到新目录：Release 下重建 Serilog logger，DEBUG 下为空操作。
                AppLog.Initialize(value);
                SaveImmediate();
                RefreshLogSizeAsync().SafeFireAndForget($"{nameof(SettingsPageViewModel)}.{nameof(RefreshLogSizeAsync)}");
            }
        }

        private string _cacheSizeText = string.Empty;
        public string CacheSizeText
        {
            get => _cacheSizeText;
            set
            {
                if (_cacheSizeText == value) return;
                _cacheSizeText = value;
                RaisePropertyChanged(nameof(CacheSizeText));
            }
        }

        private string _logSizeText = string.Empty;
        public string LogSizeText
        {
            get => _logSizeText;
            set
            {
                if (_logSizeText == value) return;
                _logSizeText = value;
                RaisePropertyChanged(nameof(LogSizeText));
            }
        }

        public string DefaultPath => Framework.Services.AppSettings.GetDefaultPath();
        public string DefaultCachePath => Framework.Services.AppSettings.GetDefaultCachePath();
        public string DefaultLogPath => Framework.Services.AppSettings.GetDefaultLogPath();

        public string CurrentVersionText => string.Format(L("Settings.CurrentVersion", "Current version: {0}"), _updateService.CurrentVersion);

        public ObservableCollection<LanguageOption> LanguageOptions => _localization.AvailableLanguages;

        public string SelectedLanguage
        {
            get => _localization.CurrentLanguage;
            set
            {
                _localization.ApplyLanguage(value);
                _localization.CurrentLanguage = value;
                _settings.Current.Language = value;
                SaveImmediate();
                CloseBehaviorOptions.ResetBindings();
            }
        }

        private DelegateCommand<string>? _selectLanguageCommand;
        public DelegateCommand<string> SelectLanguageCommand =>
            _selectLanguageCommand ??= new DelegateCommand<string>(code =>
            {
                if (string.IsNullOrEmpty(code)) return;
                SelectedLanguage = code;
            });

        public System.ComponentModel.BindingList<CloseBehaviorOption> CloseBehaviorOptions { get; } = new()
        {            new CloseBehaviorOption { Value = "MinimizeToTaskbar", DisplayNameResourceKey = "Settings.CloseBehavior.MinimizeToTaskbar" },
            new CloseBehaviorOption { Value = "ExitDirectly", DisplayNameResourceKey = "Settings.CloseBehavior.ExitDirectly" },
        };

        public string SelectedCloseBehavior
        {
            get => _settings.Current.CloseBehavior;
            set
            {
                _settings.Current.CloseBehavior = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(RememberCloseBehavior));
                SaveImmediate();
            }
        }

        private DelegateCommand<string>? _selectCloseBehaviorCommand;
        public DelegateCommand<string> SelectCloseBehaviorCommand =>
            _selectCloseBehaviorCommand ??= new DelegateCommand<string>(value =>
            {
                if (string.IsNullOrEmpty(value)) return;
                SelectedCloseBehavior = value;
            });

        public bool RememberCloseBehavior
        {
            get => _settings.Current.CloseBehaviorRemembered;
            set
            {
                _settings.Current.CloseBehaviorRemembered = value;
                RaisePropertyChanged();
                SaveImmediate();
            }
        }


        public bool ShowTeamOverlayDuringHeroSelection
        {
            get => _settings.Current.ShowTeamOverlayDuringHeroSelection;
            set
            {
                _settings.Current.ShowTeamOverlayDuringHeroSelection = value;
                RaisePropertyChanged();
                SaveImmediate();
            }
        }

        /// <summary>字体缩放档位（0=默认，每档 +1px，最大 5）。拖动滑块即时全应用生效。</summary>
        public int FontScale
        {
            get => _settings.Current.FontScale;
            set
            {
                var clamped = Math.Clamp(value, 0, IUiScaleService.MaxScale);
                if (_settings.Current.FontScale == clamped) return;
                _settings.Current.FontScale = clamped;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(FontSizeValueText));
                _uiScale.Apply(clamped);
                SaveImmediate();
            }
        }

        /// <summary>滑块下方的当前数值说明，如「当前字号：13（默认）」「当前字号：18（放大 5）」。</summary>
        public string FontSizeValueText
        {
            get
            {
                var scale = _settings.Current.FontScale;
                var px = 13 + scale; // Body 基准 13，与 UiScaleService.FontTokens 保持一致
                return scale == 0
                    ? string.Format(L("Settings.FontSize.Value.Default", "Current size: {0} px (default)"), px)
                    : string.Format(L("Settings.FontSize.Value.Scaled", "Current size: {0} px (+{1})"), px, scale);
            }
        }

        public SettingsPageViewModel(
            ISettingsService settings,
            ILocalizationService localization,
            ILocalizedTextProvider localizedText,
            IMainContentNavigationService navigation,
            IImageCacheService cacheService,
            IUpdateService updateService,
            IClipboardService clipboard,
            IUIDispatcher uiDispatcher,
            IUiScaleService uiScale)
        {
            _settings = settings;
            _localization = localization;
            _localizedText = localizedText;
            _navigation = navigation;
            _cacheService = cacheService;
            _updateService = updateService;
            _clipboard = clipboard;
            _uiDispatcher = uiDispatcher;
            _uiScale = uiScale;
            Debug.WriteLine($"[{nameof(SettingsPageViewModel)}] UpdateService 注入成功，当前版本: {_updateService.CurrentVersion}");

            _dataPath = _settings.Current.DataSavePath;
            _cachePath = _settings.Current.CachePath;
            _logPath = _settings.Current.LogPath;
            RefreshCacheSizeAsync().SafeFireAndForget($"{nameof(SettingsPageViewModel)}.{nameof(RefreshCacheSizeAsync)}");
            RefreshLogSizeAsync().SafeFireAndForget($"{nameof(SettingsPageViewModel)}.{nameof(RefreshLogSizeAsync)}");

            // 订阅配置广播：托盘菜单改动、FileSystemWatcher 检测到外部改写，都会走这里刷新 UI。
            // 事件可能在后台线程回调，Handler 内部再 marshal 到 UI 线程。
            _settings.SettingsChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // 已 Dispose 后仍收到事件（订阅解除前的最后一次触发）时静默返回
            if (_isDisposed) return;

            if (_uiDispatcher.CheckAccess())
            {
                ApplySettingsSnapshotToUi();
            }
            else
            {
                _uiDispatcher.BeginInvoke(ApplySettingsSnapshotToUi);
            }
        }

        /// <summary>
        /// 从 <see cref="_settings"/>.Current 拉取最新值刷新绑定属性。
        /// 关键点：直接写底层字段 + 手动 RaisePropertyChanged，绕过公有 setter，避免触发 SaveImmediate 形成
        /// "外部改动 → 广播 → VM 更新 → 再次 SaveAsync → 再次广播" 的循环回响。
        /// </summary>
        private void ApplySettingsSnapshotToUi()
        {
            _dataPath = _settings.Current.DataSavePath;
            _cachePath = _settings.Current.CachePath;
            _logPath = _settings.Current.LogPath;

            RaisePropertyChanged(nameof(DataPath));
            RaisePropertyChanged(nameof(CachePath));
            RaisePropertyChanged(nameof(LogPath));
            RaisePropertyChanged(nameof(SelectedCloseBehavior));
            RaisePropertyChanged(nameof(RememberCloseBehavior));
            RaisePropertyChanged(nameof(SelectedLanguage));
            RaisePropertyChanged(nameof(ShowTeamOverlayDuringHeroSelection));
            RaisePropertyChanged(nameof(FontScale));
            RaisePropertyChanged(nameof(FontSizeValueText));
        }

        private bool _isDisposed;
        protected override void Dispose(bool disposing)
        {
            if (_isDisposed) { base.Dispose(disposing); return; }
            _isDisposed = true;
            if (disposing)
            {
                _settings.SettingsChanged -= OnSettingsChanged;
            }
            base.Dispose(disposing);
        }

        protected override async void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            await _settings.ReloadAsync();

            _dataPath = _settings.Current.DataSavePath;
            _cachePath = _settings.Current.CachePath;
            _logPath = _settings.Current.LogPath;

            RaisePropertyChanged(nameof(DataPath));
            RaisePropertyChanged(nameof(CachePath));
            RaisePropertyChanged(nameof(LogPath));
            RaisePropertyChanged(nameof(SelectedCloseBehavior));
            RaisePropertyChanged(nameof(RememberCloseBehavior));
            RaisePropertyChanged(nameof(SelectedLanguage));
            RaisePropertyChanged(nameof(ShowTeamOverlayDuringHeroSelection));
            RaisePropertyChanged(nameof(FontScale));
            RaisePropertyChanged(nameof(FontSizeValueText));
            RefreshLogSizeAsync().SafeFireAndForget($"{nameof(SettingsPageViewModel)}.{nameof(RefreshLogSizeAsync)}");
            base.OnNavigatedToExecute(navigationContext);
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            // 实时落盘策略下无 pending 计时器需清理
            base.OnNavigatedFromExecute(navigationContext);
        }

        public async System.Threading.Tasks.Task RefreshCacheSizeAsync()
        {
            try
            {
                var path = _cachePath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
                {
                    CacheSizeText = "0 B";
                    return;
                }

                // BCL 限制：Directory.EnumerateFiles / FileInfo.Length 均无原生 async 等价物（截至 .NET 10），
                // 此处属于元数据级遍历，使用 Task.Run 将同步遍历卸载到线程池以避免阻塞 UI 线程。
                var size = await System.Threading.Tasks.Task.Run(() =>
                {
                    if (!System.IO.Directory.Exists(path))
                        return 0L;

                    long total = 0;
                    foreach (var file in System.IO.Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories))
                    {
                        try { total += new System.IO.FileInfo(file).Length; }
                        catch { }
                    }
                    return total;
                }).ConfigureAwait(false);

                CacheSizeText = FormatSize(size);
            }
            catch
            {
                CacheSizeText = L("Settings.CacheSizeUnknown", "Unknown");
            }
        }

        public async System.Threading.Tasks.Task RefreshLogSizeAsync()
        {
            try
            {
                var path = _logPath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
                {
                    LogSizeText = "0 B";
                    return;
                }

                // 只统计 *.log（与"清空日志"删除范围一致），Task.Run 卸载同步遍历到线程池，避免阻塞 UI 线程。
                var size = await System.Threading.Tasks.Task.Run(() =>
                {
                    if (!System.IO.Directory.Exists(path))
                        return 0L;

                    long total = 0;
                    foreach (var file in System.IO.Directory.EnumerateFiles(path, "*.log", System.IO.SearchOption.TopDirectoryOnly))
                    {
                        try { total += new System.IO.FileInfo(file).Length; }
                        catch { }
                    }
                    return total;
                }).ConfigureAwait(false);

                LogSizeText = FormatSize(size);
            }
            catch
            {
                LogSizeText = L("Settings.CacheSizeUnknown", "Unknown");
            }
        }

        private static string FormatSize(long size) => size switch
        {
            < 1024 => $"{size} B",
            < 1024 * 1024 => $"{size / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{size / (1024.0 * 1024):F1} MB",
            _ => $"{size / (1024.0 * 1024 * 1024):F2} GB"
        };

        private DelegateCommand? _browseDataPathCommand;
        public DelegateCommand BrowseDataPathCommand =>
            _browseDataPathCommand ??= new DelegateCommand(async () =>
            {
                // async DelegateCommand 等价 async void：必须顶层 try/catch 兜底，否则异常冒泡 DispatcherUnhandledException
                try
                {
                    var dialog = new OpenFolderDialog
                    {
                        Title = L("Settings.BrowseDataPath", "Select data save path"),
                        InitialDirectory = string.IsNullOrWhiteSpace(DataPath) ? DefaultPath : DataPath
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        var oldPath = _settings.Current.DataSavePath;
                        DataPath = dialog.FolderName;
                        if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, DataPath, System.StringComparison.OrdinalIgnoreCase))
                        {
                            await MigrateFolderAsync(oldPath, DataPath);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(SettingsPageViewModel)}.{nameof(BrowseDataPathCommand)}");
                }
            });


        private DelegateCommand? _browseCachePathCommand;
        public DelegateCommand BrowseCachePathCommand =>
            _browseCachePathCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    var dialog = new OpenFolderDialog
                    {
                        Title = L("Settings.BrowseCachePath", "Select cache path"),
                        InitialDirectory = string.IsNullOrWhiteSpace(CachePath) ? DefaultCachePath : CachePath
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        var oldPath = _settings.Current.CachePath;
                        CachePath = dialog.FolderName;
                        if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, CachePath, System.StringComparison.OrdinalIgnoreCase))
                        {
                            await MigrateFolderAsync(oldPath, CachePath);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(SettingsPageViewModel)}.{nameof(BrowseCachePathCommand)}");
                }
            });


        private DelegateCommand? _browseLogPathCommand;
        public DelegateCommand BrowseLogPathCommand =>
            _browseLogPathCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    var dialog = new OpenFolderDialog
                    {
                        Title = L("Settings.BrowseLogPath", "Select log path"),
                        InitialDirectory = string.IsNullOrWhiteSpace(LogPath) ? DefaultLogPath : LogPath
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        var oldPath = _settings.Current.LogPath;
                        LogPath = dialog.FolderName;
                        if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, LogPath, System.StringComparison.OrdinalIgnoreCase))
                        {
                            await MigrateFolderAsync(oldPath, LogPath);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(SettingsPageViewModel)}.{nameof(BrowseLogPathCommand)}");
                }
            });


        private DelegateCommand? _clearCacheCommand;
        public DelegateCommand ClearCacheCommand =>
            _clearCacheCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    await _cacheService.ClearCacheAsync();
                    await RefreshCacheSizeAsync();
                    eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(L("Settings.CacheCleared", "Cache cleared")));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(SettingsPageViewModel)}.{nameof(ClearCacheCommand)}");
                }
            });

        private DelegateCommand? _clearLogsCommand;
        public DelegateCommand ClearLogsCommand =>
            _clearLogsCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    await AppLog.ClearLogsAsync(LogPath);
                    await RefreshLogSizeAsync();
                    eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(L("Settings.LogsCleared", "Logs cleared")));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(SettingsPageViewModel)}.{nameof(ClearLogsCommand)}");
                }
            });

        private static async System.Threading.Tasks.Task MigrateFolderAsync(string oldPath, string newPath)
        {
            // BCL 限制：File.Move / File.Delete / Directory.CreateDirectory / Directory.Move / Directory.Delete /
            // Directory.EnumerateFiles / Directory.EnumerateDirectories 均无原生 async 等价物（截至 .NET 10），
            // 均为文件系统元数据级操作，使用 Task.Run 卸载到线程池以避免阻塞 UI 线程。
            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!System.IO.Directory.Exists(oldPath)) return;
                    if (!System.IO.Directory.Exists(newPath))
                        System.IO.Directory.CreateDirectory(newPath);

                    foreach (var file in System.IO.Directory.EnumerateFiles(oldPath))
                    {
                        var dest = System.IO.Path.Combine(newPath, System.IO.Path.GetFileName(file));
                        if (System.IO.File.Exists(dest))
                            System.IO.File.Delete(dest);
                        System.IO.File.Move(file, dest);
                    }
                    foreach (var dir in System.IO.Directory.EnumerateDirectories(oldPath))
                    {
                        var dest = System.IO.Path.Combine(newPath, System.IO.Path.GetFileName(dir));
                        if (System.IO.Directory.Exists(dest))
                            System.IO.Directory.Delete(dest, true);
                        System.IO.Directory.Move(dir, dest);
                    }
                }
                catch { }
            }).ConfigureAwait(false);
        }
    }

    public class CloseBehaviorOption
    {
        public string Value { get; set; } = string.Empty;
        public string DisplayNameResourceKey { get; set; } = string.Empty;
        public string DisplayName =>
            System.Windows.Application.Current?.TryFindResource(DisplayNameResourceKey) as string ?? DisplayNameResourceKey;
    }
}

