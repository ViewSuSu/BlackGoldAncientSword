using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
using NarakaBladepoint.StatsAssistant.Framework.Services;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class LocalizationService : ILocalizationService
    {
        private string _currentLanguage = "zh-CN";

        private static readonly string StringDictUri =
            "/NarakaBladepoint.StatsAssistant.Resources;component/Themes/Strings.{0}.xaml";

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<LanguageOption> AvailableLanguages { get; } = new()
        {
            new LanguageOption { Code = "zh-CN", DisplayName = "中文简体" },
            new LanguageOption { Code = "zh-TW", DisplayName = "中文繁體" },
            new LanguageOption { Code = "en",    DisplayName = "English" },
        };

        public void ApplyLanguage(ResourceDictionary appResources, string language)
        {
            var uri = new Uri(string.Format(StringDictUri, language), UriKind.Relative);
            var newDict = new ResourceDictionary { Source = uri };

            for (int i = appResources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var source = appResources.MergedDictionaries[i].Source?.ToString() ?? "";
                if (source.Contains("Strings."))
                    appResources.MergedDictionaries.RemoveAt(i);
            }

            appResources.MergedDictionaries.Add(newDict);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
