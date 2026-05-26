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
            }
        }

        private DelegateCommand<string>? _selectLanguageCommand;
        public DelegateCommand<string> SelectLanguageCommand =>
            _selectLanguageCommand ??= new DelegateCommand<string>(code =>
            {
                if (string.IsNullOrEmpty(code)) return;
                SelectedLanguage = code;
            });

        public SettingsPageViewModel(
            ISettingsService settings,
            ILocalizationService localization,
            IMainContentNavigationService navigation,
            IImageCacheService cacheService)
        {
            _settings = settings;
            _localization = localization;
            _navigation = navigation;
            _cacheService = cacheService;
            _dataPath = string.IsNullOrWhiteSpace(settings.Current.DataSavePath) ? DefaultPath : settings.Current.DataSavePath;
            _cachePath = string.IsNullOrWhiteSpace(settings.Current.CachePath) ? DefaultCachePath : settings.Current.CachePath;
            _originalDataPath = _dataPath;
            _originalCachePath = _cachePath;
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
}