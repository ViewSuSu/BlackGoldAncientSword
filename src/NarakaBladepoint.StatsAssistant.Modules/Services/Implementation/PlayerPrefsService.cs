using System.Threading.Tasks;
using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
using NarakaBladepoint.StatsAssistant.Framework.Services;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Modules.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class PlayerPrefsService : IPlayerPrefsService
    {
        private static readonly string FilePath =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "24Entertainment", "Naraka", "player_prefs.txt");

        public PlayerPrefsData Current { get; private set; } = new();

        public PlayerPrefsService()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath))
                    return;

                var lines = System.IO.File.ReadAllLines(FilePath);
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
                // silently ignore parse errors
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