using System.Collections.ObjectModel;
using System.ComponentModel;
using NarakaBladepoint.StatsAssistant.Framework.Services;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions
{
    public interface ILocalizationService : INotifyPropertyChanged
    {
        string CurrentLanguage { get; set; }
        ObservableCollection<LanguageOption> AvailableLanguages { get; }

        void ApplyLanguage(System.Windows.ResourceDictionary appResources, string language);
    }
}
