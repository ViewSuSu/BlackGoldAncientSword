namespace NarakaBladepoint.StatsAssistant.GameMonitor.Services.Implementation
{
    using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
    using NarakaBladepoint.StatsAssistant.Framework.Core.Extensions;

    /// <summary>
    /// 玩家偏好数据服务。从永劫无间的 player_prefs.txt 异步读取玩家信息。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class PlayerPrefsService : IPlayerPrefsService
    {
        private static readonly string FilePath =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "24Entertainment", "Naraka", "player_prefs.txt");

        public PlayerPrefsData Current { get; private set; } = new();

        public PlayerPrefsService()
        {
            LoadAsync().SafeFireAndForget("PlayerPrefsService.LoadAsync");
        }

        /// <summary>
        /// 异步加载玩家偏好数据。
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath))
                    return;

                var lines = await System.IO.File.ReadAllLinesAsync(FilePath);
                var result = new PlayerPrefsData();

                foreach (var line in lines)
                {
                    var eqIdx = line.IndexOf('=');
                    if (eqIdx < 0) continue;

                    var sectionKey = line.Substring(0, eqIdx);
                    var sectionValue = line.Substring(eqIdx + 1);

                    if (sectionKey.StartsWith("account_prefs_"))
                    {
                        var pairs = ParseSemicolonPairs(sectionValue);
                        foreach (var kv in pairs)
                        {
                            switch (kv.Key)
                            {
                                case "player_name": result.PlayerName = kv.Value; break;
                                case "player_id": result.PlayerId = kv.Value; break;
                                case "player_level":
                                    if (int.TryParse(kv.Value, out var level)) result.PlayerLevel = level;
                                    break;
                            }
                        }
                    }
                    else if (sectionKey == "global_prefs_key")
                    {
                        var pairs = ParseSemicolonPairs(sectionValue);
                        foreach (var kv in pairs)
                        {
                            switch (kv.Key)
                            {
                                case "serverId": result.ServerId = kv.Value; break;
                                case "maxMember":
                                    if (int.TryParse(kv.Value, out var max)) result.MaxMember = max;
                                    break;
                            }
                        }
                    }
                }

                result.IsLoaded = true;
                Current = result;
            }
            catch
            {
                // 静默忽略解析错误，不影响主流程
            }
        }

        private static Dictionary<string, string> ParseSemicolonPairs(string value)
        {
            var dict = new Dictionary<string, string>();
            var pairs = value.Split(';');
            foreach (var pair in pairs)
            {
                var commaIdx = pair.IndexOf(',');
                if (commaIdx < 0) continue;
                var key = pair.Substring(0, commaIdx);
                var val = pair.Substring(commaIdx + 1);
                dict[key] = val;
            }
            return dict;
        }
    }
}
