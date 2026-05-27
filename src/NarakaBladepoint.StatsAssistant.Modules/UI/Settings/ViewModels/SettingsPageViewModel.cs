using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using NarakaBladepoint.StatsAssistant.Framework.Core.Bases.ViewModels;
using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;
using NarakaBladepoint.StatsAssistant.Framework.Core.Events;
using NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Modules.UI.Settings.ViewModels
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
                _ = _settings.SaveAsync();
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
                _ = _settings.SaveAsync();
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
                _ = _settings.SaveAsync();
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

        private DelegateCommand? _checkForUpdatesCommand;
        public DelegateCommand CheckForUpdatesCommand =>
            _checkForUpdatesCommand ??= new DelegateCommand(async () =>
            {
                await _updateService.CheckForUpdatesAsync();
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
                _ = _settings.SaveAsync();
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
                _ = _settings.SaveAsync();
            }
        }

        public bool AutoCheckUpdates
        {
            get => _settings.Current.AutoCheckUpdates;
            set
            {
                _settings.Current.AutoCheckUpdates = value;
                RaisePropertyChanged();
                _ = _settings.SaveAsync();
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

            _dataPath = _settings.Current.DataSavePath;
            _cachePath = _settings.Current.CachePath;

            _ = RefreshCacheSizeAsync();

            eventAggregator.GetEvent<SettingsChangedEvent>().Subscribe(() =>
            {
                RaisePropertyChanged(nameof(RememberCloseBehavior));
                RaisePropertyChanged(nameof(SelectedCloseBehavior));
            });
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            RaisePropertyChanged(nameof(RememberCloseBehavior));
            RaisePropertyChanged(nameof(SelectedCloseBehavior));
            _ = RefreshCacheSizeAsync();
        }

        private async System.Threading.Tasks.Task RefreshCacheSizeAsync()
        {
            try
            {
                var size = await System.Threading.Tasks.Task.Run(() =>
                {
                    var path = _cacheService.CachePath;
                    if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
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
            _browseDataPathCommand ??= new DelegateCommand(() =>
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
                        _ = MigrateFolderAsync(oldPath, DataPath);
                    }
                }
            });

        private DelegateCommand? _browseCachePathCommand;
        public DelegateCommand BrowseCachePathCommand =>
            _browseCachePathCommand ??= new DelegateCommand(() =>
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
                        _ = MigrateFolderAsync(oldPath, CachePath);
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
