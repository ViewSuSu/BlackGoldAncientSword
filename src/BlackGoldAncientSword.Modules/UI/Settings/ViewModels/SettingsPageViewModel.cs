using System.Collections.ObjectModel;
using System.Windows;
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
        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
        private readonly ISettingsService _settings;
        private readonly ILocalizationService _localization;
        private readonly IMainContentNavigationService _navigation;
        private readonly IImageCacheService _cacheService;
        private readonly IUpdateService _updateService;

        private System.Threading.Timer? _saveTimer;
        private const int SaveDebounceMs = 300;

        /// <summary>鐎点倖鍎肩换婊勭┍濠靛棛鎽犻柨娑樿嫰閹酣鐛崜浣哄彋闁哄啫鐖煎Λ鍧楀礃閸涙壆绠剧紓渚囧幒閹便劑寮ㄩ柅娑滅濞戞挴鍋撴繛鍡忊偓鍐叉櫢闁烩晜菧閳?/summary>
        private void DebouncedSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = new System.Threading.Timer(_ =>
            {
                _settings.SaveAsync().SafeFireAndForget("Settings.SaveAsync");
            }, null, SaveDebounceMs, System.Threading.Timeout.Infinite);
        }

        private string _dataPath = string.Empty;
        public string DataPath
        {
            get => _dataPath;
            set
            {
                if (!SetProperty(ref _dataPath, value))
                    return;

                if (!string.IsNullOrWhiteSpace(value) && !System.IO.Directory.Exists(value))
                    return;

                _settings.Current.DataSavePath = value;
                DebouncedSave();
            }
        }

        private string _cachePath = string.Empty;
        public string CachePath
        {
            get => _cachePath;
            set
            {
                if (!SetProperty(ref _cachePath, value))
                    return;

                if (!string.IsNullOrWhiteSpace(value) && !System.IO.Directory.Exists(value))
                    return;

                _settings.Current.CachePath = value;
                _cacheService.CachePath = value;
                DebouncedSave();
            }
        }

        private string _cacheSizeText = string.Empty;
        public string CacheSizeText
        {
            get => _cacheSizeText;
            set => SetProperty(ref _cacheSizeText, value);
        }

        public string DefaultPath => Framework.Services.AppSettings.GetDefaultPath();
        public string DefaultCachePath => Framework.Services.AppSettings.GetDefaultCachePath();

        public string CurrentVersionText => string.Format(L("Settings.CurrentVersion", "Current version: {0}"), _updateService.CurrentVersion);

        public ObservableCollection<LanguageOption> LanguageOptions => _localization.AvailableLanguages;

        public string SelectedLanguage
        {
            get => _localization.CurrentLanguage;
            set
            {
                _localization.ApplyLanguage(Application.Current.Resources, value);
                _localization.CurrentLanguage = value;
                _settings.Current.Language = value;
                DebouncedSave();
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
                DebouncedSave();
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
                DebouncedSave();
            }
        }

        public bool AutoCheckUpdates
        {
            get => _settings.Current.AutoCheckUpdates;
            set
            {
                _settings.Current.AutoCheckUpdates = value;
                RaisePropertyChanged();
                DebouncedSave();
            }
        }

        public SettingsPageViewModel(
            ISettingsService settings,
            ILocalizationService localization,
            IMainContentNavigationService navigation,
            IImageCacheService cacheService,
            IUpdateService updateService)
        {
            _settings = settings;
            _localization = localization;
            _navigation = navigation;
            _cacheService = cacheService;
            _updateService = updateService;
            Debug.WriteLine($"[SettingsPageVM] UpdateService 瀹稿弶鏁為崗銉礉瑜版挸澧犻悧鍫熸拱: {_updateService.CurrentVersion}");

            _dataPath = _settings.Current.DataSavePath;
            _cachePath = _settings.Current.CachePath;
            RefreshCacheSizeAsync().SafeFireAndForget("Settings.RefreshCacheSize");
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            RaisePropertyChanged(nameof(SelectedCloseBehavior));
            RaisePropertyChanged(nameof(RememberCloseBehavior));
            RaisePropertyChanged(nameof(AutoCheckUpdates));
            base.OnNavigatedToExecute(navigationContext);
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
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
                });

                CacheSizeText = size switch
                {
                    < 1024 => $"{size} B",
                    < 1024 * 1024 => $"{size / 1024.0:F1} KB",
                    < 1024 * 1024 * 1024 => $"{size / (1024.0 * 1024):F1} MB",
                    _ => $"{size / (1024.0 * 1024 * 1024):F2} GB"
                };
            }
            catch
            {
                CacheSizeText = L("Settings.CacheSizeUnknown", "Unknown");
            }
        }

        private DelegateCommand? _browseDataPathCommand;
        public DelegateCommand BrowseDataPathCommand =>
            _browseDataPathCommand ??= new DelegateCommand(async () =>
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
            });


        private DelegateCommand? _browseCachePathCommand;
        public DelegateCommand BrowseCachePathCommand =>
            _browseCachePathCommand ??= new DelegateCommand(async () =>
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
            });


        private DelegateCommand? _clearCacheCommand;
        public DelegateCommand ClearCacheCommand =>
            _clearCacheCommand ??= new DelegateCommand(async () =>
            {
                await _cacheService.ClearCacheAsync();
                await RefreshCacheSizeAsync();
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(L("Settings.CacheCleared", "Cache cleared")));
            });

        private static async System.Threading.Tasks.Task MigrateFolderAsync(string oldPath, string newPath)
        {
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
            });
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

