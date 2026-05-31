namespace BlackGoldAncientSword.Framework.Services
{
    public class AppSettings
    {
        public string DataSavePath { get; set; } = string.Empty;
        public string CachePath { get; set; } = string.Empty;

        public string Language { get; set; } = "zh-CN";

        public string CloseBehavior { get; set; } = "MinimizeToTaskbar";

        public bool CloseBehaviorRemembered { get; set; } = false;

        public bool AutoCheckUpdates { get; set; } = true;

        public string GameLogPath { get; set; } = GetDefaultGameLogPath();

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

        public static string GetDefaultGameLogPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "24Entertainment", "Naraka",
                "Player.log");
        }
    }
}
