using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class LocalizationService : ILocalizationService
    {
        private string _currentLanguage = "zh-CN";

        private static readonly string StringDictUri =
            "/BlackGoldAncientSword.Resources;component/Themes/Strings.{0}.xaml";

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

        /// <summary>
        /// 切换语言资源字典。
        /// 线程契约：**必须在 UI 线程调用**。修改 <see cref="Application.Current"/>.Resources.MergedDictionaries
        /// 在 WPF 内部不是线程安全的，方法首部使用 <see cref="DispatcherObject.VerifyAccess"/> 强制断言；
        /// 后台线程调用会立即抛 <see cref="InvalidOperationException"/>。
        /// </summary>
        public void ApplyLanguage(string language)
        {
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher?.VerifyAccess();

            var appResources = app.Resources;
            if (appResources == null) return;

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
