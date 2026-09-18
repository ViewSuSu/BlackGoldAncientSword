using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using BlackGoldAncientSword.Modules.UI.Stats.Services;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxRawDumpProbe
    {
        private const string OutDir = @"C:\tmp\heybox-raw";

        private readonly ITestOutputHelper _output;

        public HeyboxRawDumpProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_DumpRawJson()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            var prefs = new PlayerPrefsService();
            await prefs.LoadAsync();
            var roleId = prefs.Current.PlayerId;
            var name = prefs.Current.OriginalPlayerName;

            var stats = new PlayerStatsLoader(new HeyboxHomeDataProvider(new HeyboxRequestCache()), new HeyboxRequestCache());
            var search = await stats.SearchLocalPlayerAsync(roleId, name, CancellationToken.None);
            Assert.NotNull(search);
            var server = search!.Server;
            _output.WriteLine($"role_id={roleId}  server={server}  name={name}");

            Directory.CreateDirectory(OutDir);

            await Dump("home.json", $"/game/yjwj/home/data?role_id={Uri.EscapeDataString(roleId)}&server={Uri.EscapeDataString(server)}");

            await Dump("home-season.json",
                $"/game/yjwj/home/data?role_id={Uri.EscapeDataString(roleId)}&server={Uri.EscapeDataString(server)}" +
                "&battle_tid=5000001&season=pre-01");

            var listRaw = await Dump("matchlist.json",
                $"/game/yjwj/match/list?role_id={Uri.EscapeDataString(roleId)}&server={Uri.EscapeDataString(server)}&limit=20&offset=0");

            var matchId = TryReadFirstMatchId(listRaw);
            if (matchId is not null)
            {
                await Dump("detail.json",
                    $"/game/yjwj/match/detail?match_id={Uri.EscapeDataString(matchId)}");
            }
            else
            {
                _output.WriteLine("!! 列表里没解析出 match_id，详情跳过");
            }

            _output.WriteLine($"raw json 已写到 {OutDir}");
        }

        private async Task<string?> Dump(string fileName, string pathAndQuery)
        {
            try
            {
                var raw = await NarakaApiClient.Http.GetStringAsync(pathAndQuery).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(OutDir, fileName), raw).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() : "?";
                var len = root.TryGetProperty("result", out var r) ? r.GetRawText().Length : 0;
                _output.WriteLine($"[{fileName}] {raw.Length} 字节  status={status}  result={len} 字符");

                return raw;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{fileName}] ERR {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static string? TryReadFirstMatchId(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
                if (!result.TryGetProperty("match_list", out var list) || list.ValueKind != JsonValueKind.Array) return null;
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("match_id", out var id) && id.ValueKind == JsonValueKind.String)
                        return id.GetString();
                }
            }
            catch (JsonException) { }
            return null;
        }
    }
}
