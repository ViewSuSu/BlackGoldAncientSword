namespace NarakaBladepoint.StatsAssistant.Framework.Services
{
    public class AppSettings
    {
        public string DataSavePath { get; set; } = string.Empty;
        public string CachePath { get; set; } = string.Empty;

        public string Language { get; set; } = "zh-CN";

        public static string GetDefaultCachePath()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(basePath, "NarakaBladepointStatsAssistant", "cache");
        }

        public static string GetDefaultPath()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(basePath, "NarakaBladepointStatsAssistant");
        }
    }
}