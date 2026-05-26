namespace NarakaBladepoint.StatsAssistant.Framework.Services
{
    public class AppSettings
    {
        public string DataSavePath { get; set; } = string.Empty;
        public string CachePath { get; set; } = string.Empty;

        public string Language { get; set; } = "zh-CN";

        /// <summary>
        /// "Ask" | "MinimizeToTaskbar" | "ExitDirectly"
        /// </summary>
        public string CloseBehavior { get; set; } = "Ask";

        /// <summary>
        /// When true, CloseBehavior is used directly without showing the prompt dialog.
        /// </summary>
        public bool CloseBehaviorRemembered { get; set; } = false;

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