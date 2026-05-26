using System.Threading.Tasks;
using Newtonsoft.Json;
using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class SettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        private string FilePath => System.IO.Path.Combine(
            AppSettings.GetDefaultPath(), "settings.json");

        public SettingsService()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                // Ensure cache directory exists
                var cachePath = Current.CachePath;
                if (string.IsNullOrEmpty(cachePath))
                    cachePath = AppSettings.GetDefaultCachePath();
                if (!System.IO.Directory.Exists(cachePath))
                    System.IO.Directory.CreateDirectory(cachePath);
                Current.CachePath = cachePath;

                if (System.IO.File.Exists(FilePath))
                {
                    var json = System.IO.File.ReadAllText(FilePath);
                    Current = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    Current = new AppSettings
                    {
                        DataSavePath = AppSettings.GetDefaultPath(),
                        CachePath = AppSettings.GetDefaultCachePath(),
                        Language = "zh-CN"
                    };
                }
            }
            catch
            {
                Current = new AppSettings();
            }
        }

        public async Task SaveAsync()
        {
            await Task.Run(() =>
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                System.IO.File.WriteAllTextAsync(FilePath, json).GetAwaiter().GetResult();
            });
        }
    }
}