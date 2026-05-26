using System.Collections.ObjectModel;
using System.ComponentModel;
using NarakaBladepoint.StatsAssistant.Modules.Services;

namespace NarakaBladepoint.StatsAssistant.Modules.Services.Abstractions
{
    public interface ILocalizationService : INotifyPropertyChanged
    {
        string CurrentLanguage { get; set; }
        ObservableCollection<LanguageOption> AvailableLanguages { get; }

        void ApplyLanguage(System.Windows.ResourceDictionary appResources, string language);
    }
}