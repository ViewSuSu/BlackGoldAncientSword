namespace BlackGoldAncientSword.Framework.Services
{
    public class AppSettings
    {
        public string DataSavePath { get; set; } = string.Empty;
        public string CachePath { get; set; } = string.Empty;

        public string Language { get; set; } = "zh-CN";

        /// <summary>
        /// "MinimizeToTaskbar" | "MinimizeToTaskbar" | "ExitDirectly"
        /// </summary>
        public string CloseBehavior { get; set; } = "MinimizeToTaskbar";

        /// <summary>
        /// When true, CloseBehavior is used directly without showing the prompt dialog.
        /// </summary>
        public bool CloseBehaviorRemembered { get; set; } = false;

        /// <summary>
        // 是否在程序启动时自动检查更新。
        /// </summary>
        public bool AutoCheckUpdates { get; set; } = false;

        public static string GetDefaultCachePath()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(basePath, "BlackGoldAncientSword", "cache");
        }

        public static string GetDefaultPath()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return System.IO.Path.Combine(basePath, "BlackGoldAncientSword");
        }
    }
}
