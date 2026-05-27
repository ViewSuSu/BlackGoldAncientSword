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
            // 构造时触发异步加载，不阻塞 UI
            _ = LoadAsync();
        }

        /// <summary>
        /// 异步从 settings.json 加载配置。
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                // 确保缓存目录存在
                var cachePath = AppSettings.GetDefaultCachePath();
                if (!System.IO.Directory.Exists(cachePath))
                    System.IO.Directory.CreateDirectory(cachePath);

                if (System.IO.File.Exists(FilePath))
                {
                    var json = await System.IO.File.ReadAllTextAsync(FilePath);
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

        /// <summary>
        /// 异步保存配置到 settings.json。
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                await System.IO.File.WriteAllTextAsync(FilePath, json);
            }
            catch { }
        }
    }
}
