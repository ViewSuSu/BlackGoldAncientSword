using System.Collections.ObjectModel;
using System.ComponentModel;
using BlackGoldAncientSword.Framework.Services;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface ILocalizationService : INotifyPropertyChanged
    {
        string CurrentLanguage { get; set; }
        ObservableCollection<LanguageOption> AvailableLanguages { get; }

        void ApplyLanguage(System.Windows.ResourceDictionary appResources, string language);
    }
}
