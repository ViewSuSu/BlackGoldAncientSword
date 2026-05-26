using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using NarakaBladepoint.StatsAssistant.Framework.Core.Bases.ViewModels;
using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;
using NarakaBladepoint.StatsAssistant.Framework.Core.Events;
using NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;
using NarakaBladepoint.StatsAssistant.Modules.Services;
using NarakaBladepoint.StatsAssistant.Modules.Services.Abstractions;

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
                if (SetProperty(ref _dataPath, value))
                    RaisePropertyChanged(nameof(HasChanges));
            }
        }

        private string _cachePath = string.Empty;
        private string _originalDataPath = string.Empty;
        private string _originalCachePath = string.Empty;
        public string CachePath
        {
            get => _cachePath;
            set
            {
                if (SetProperty(ref _cachePath, value))
                    RaisePropertyChanged(nameof(HasChanges));
            }
        }

        public bool HasChanges =>
            !string.Equals(_dataPath, _originalDataPath, System.StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_cachePath, _originalCachePath, System.StringComparison.OrdinalIgnoreCase);

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
                _localization.CurrentLanguage = value;
                _localization.ApplyLanguage(Application.Current.Resources, value);
                _settings.Current.Language = value;
                _ = System.Threading.Tasks.Task.Run(() => _settings.SaveAsync());
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
        {
            new CloseBehaviorOption { Value = "Ask", DisplayNameResourceKey = "Settings.CloseBehavior.Ask" },
            new CloseBehaviorOption { Value = "MinimizeToTaskbar", DisplayNameResourceKey = "Settings.CloseBehavior.MinimizeToTaskbar" },
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
            // Sync language from settings to localization service
            if (_settings.Current.Language != _localization.CurrentLanguage)
            {
                _localization.CurrentLanguage = _settings.Current.Language;
                _localization.ApplyLanguage(Application.Current.Resources, _settings.Current.Language);
            }
            RaisePropertyChanged(nameof(SelectedLanguage));
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
            }
        }

        private DelegateCommand? _saveCloseBehaviorCommand;
        public DelegateCommand SaveCloseBehaviorCommand =>
            _saveCloseBehaviorCommand ??= new DelegateCommand(async () =>
            {
                await _settings.SaveAsync();
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(L("Settings.SaveSuccess", "保存成功")));
            });

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
            _dataPath = string.IsNullOrWhiteSpace(settings.Current.DataSavePath) ? DefaultPath : settings.Current.DataSavePath;
            _cachePath = string.IsNullOrWhiteSpace(settings.Current.CachePath) ? DefaultCachePath : settings.Current.CachePath;
            _originalDataPath = _dataPath;
            _originalCachePath = _cachePath;
            _ = System.Threading.Tasks.Task.Run(RefreshCacheSizeAsync);
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            // Reload from settings.json to ensure latest data
            _settings.Load();
            _dataPath = string.IsNullOrWhiteSpace(_settings.Current.DataSavePath) ? DefaultPath : _settings.Current.DataSavePath;
            _cachePath = string.IsNullOrWhiteSpace(_settings.Current.CachePath) ? DefaultCachePath : _settings.Current.CachePath;
            _originalDataPath = _dataPath;
            _originalCachePath = _cachePath;
            RaisePropertyChanged(nameof(DataPath));
            RaisePropertyChanged(nameof(CachePath));
            RaisePropertyChanged(nameof(HasChanges));
            RaisePropertyChanged(nameof(SelectedCloseBehavior));
            RaisePropertyChanged(nameof(RememberCloseBehavior));
            // Sync language from settings to localization service
            if (_settings.Current.Language != _localization.CurrentLanguage)
            {
                _localization.CurrentLanguage = _settings.Current.Language;
                _localization.ApplyLanguage(Application.Current.Resources, _settings.Current.Language);
            }
            RaisePropertyChanged(nameof(SelectedLanguage));
            CloseBehaviorOptions.ResetBindings();
            _ = System.Threading.Tasks.Task.Run(RefreshCacheSizeAsync);
        }

        private async System.Threading.Tasks.Task RefreshCacheSizeAsync()
        {
            var bytes = await _cacheService.GetCacheSizeBytesAsync();
            CacheSizeText = FormatSize(bytes);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private DelegateCommand? _browseFolderCommand;
        public DelegateCommand BrowseFolderCommand =>
            _browseFolderCommand ??= new DelegateCommand(() =>
            {
                var dialog = new OpenFolderDialog
                {
                    Title = L("Settings.BrowseDataPath", "选择数据保存路径"),
                    InitialDirectory = string.IsNullOrWhiteSpace(DataPath) ? DefaultPath : DataPath
                };
                if (dialog.ShowDialog() == true)
                {
                    DataPath = dialog.FolderName;
                }
            });

        private DelegateCommand? _browseCacheFolderCommand;
        public DelegateCommand BrowseCacheFolderCommand =>
            _browseCacheFolderCommand ??= new DelegateCommand(() =>
            {
                var dialog = new OpenFolderDialog
                {
                    Title = L("Settings.BrowseCachePath", "选择缓存路径"),
                    InitialDirectory = string.IsNullOrWhiteSpace(CachePath) ? DefaultCachePath : CachePath
                };
                if (dialog.ShowDialog() == true)
                {
                    CachePath = dialog.FolderName;
                }
            });

        private DelegateCommand? _clearCacheCommand;
        public DelegateCommand ClearCacheCommand =>
            _clearCacheCommand ??= new DelegateCommand(async () =>
            {
                await _cacheService.ClearCacheAsync();
                await RefreshCacheSizeAsync();
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(L("Settings.CacheCleared", "缓存已清除")));
            });

        private DelegateCommand? _saveCommand;
        public DelegateCommand SaveCommand =>
            _saveCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    var oldDataPath = _settings.Current.DataSavePath;
                    var oldCachePath = _settings.Current.CachePath;
                    var newDataPath = DataPath;
                    var newCachePath = CachePath;

                    // Validate paths
                    if (!string.IsNullOrWhiteSpace(newDataPath) && !System.IO.Directory.Exists(newDataPath))
                    {
                        eventAggregator.GetEvent<TipMessageEvent>()
                            .Publish(new TipMessageWithHighlightArgs(L("Settings.InvalidDataPath", "数据保存路径无效或不存在，请检查后重试"), new List<string> { "Error" }));
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(newCachePath) && !System.IO.Directory.Exists(newCachePath))
                    {
                        eventAggregator.GetEvent<TipMessageEvent>()
                            .Publish(new TipMessageWithHighlightArgs(L("Settings.InvalidCachePath", "缓存路径无效或不存在，请检查后重试"), new List<string> { "Error" }));
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(newDataPath))
                        _settings.Current.DataSavePath = newDataPath;
                    if (!string.IsNullOrWhiteSpace(newCachePath))
                        _settings.Current.CachePath = newCachePath;

                    await _settings.SaveAsync();

                    // Migrate files if paths changed
                    if (!string.IsNullOrWhiteSpace(oldDataPath) && !string.IsNullOrWhiteSpace(newDataPath)
                        && !string.Equals(oldDataPath, newDataPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        await MigrateFolderAsync(oldDataPath, newDataPath);
                    }
                    if (!string.IsNullOrWhiteSpace(oldCachePath) && !string.IsNullOrWhiteSpace(newCachePath)
                        && !string.Equals(oldCachePath, newCachePath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        await MigrateFolderAsync(oldCachePath, newCachePath);
                        _cacheService.CachePath = newCachePath;
                    }

                    await RefreshCacheSizeAsync();
                    eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(L("Settings.SaveSuccess", "保存成功")));
                }
                catch (Exception ex)
                {
                    eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(string.Format(L("Settings.SaveFailed", "保存失败: {0}"), ex.Message), new List<string> { "Error" }));
                }
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

                    foreach (var file in System.IO.Directory.GetFiles(oldPath))
                    {
                        var dest = System.IO.Path.Combine(newPath, System.IO.Path.GetFileName(file));
                        if (System.IO.File.Exists(dest))
                            System.IO.File.Delete(dest);
                        System.IO.File.Move(file, dest);
                    }
                    foreach (var dir in System.IO.Directory.GetDirectories(oldPath))
                    {
                        var dest = System.IO.Path.Combine(newPath, System.IO.Path.GetFileName(dir));
                        if (System.IO.Directory.Exists(dest))
                            System.IO.Directory.Delete(dest, true);
                        System.IO.Directory.Move(dir, dest);
                    }
                    // Don't delete old path - keep as backup
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
