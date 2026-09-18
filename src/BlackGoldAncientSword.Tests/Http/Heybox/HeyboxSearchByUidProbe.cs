using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxSearchByUidProbe
    {
        private readonly ITestOutputHelper _output;

        public HeyboxSearchByUidProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_Which_Search_Accepts_RoleId()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            var prefs = new PlayerPrefsService();
            await prefs.LoadAsync();
            var roleId = prefs.Current.PlayerId;
            var name = prefs.Current.OriginalPlayerName;
            var bareId = StripPrefix(roleId);

            _output.WriteLine($"本机 role_id={roleId}  bareId={bareId}  name={name}");

            foreach (var (label, path) in new[]
            {
                ("player_search/do", "/game/player_search/do"),
                ("yjwj/search", "/game/yjwj/search"),
            })
            {
                foreach (var (qLabel, q) in new[]
                {
                    ("role_id(带前缀)", roleId),
                    ("bareId(不带前缀)", bareId),
                    ("昵称", name),
                })
                {
                    await Probe($"{label} | q={qLabel}", $"{path}?q={Uri.EscapeDataString(q)}");
                }
            }
        }

        private async Task Probe(string label, string pathAndQuery)
        {
            try
            {
                var sep = pathAndQuery.Contains('?') ? "&" : "?";
                var raw = await NarakaApiClient.Http.GetStringAsync(pathAndQuery + sep + "game_type=yjwj&offset=0&limit=5")
                    .ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() : "?";
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "";

                var count = 0;
                string first = "";
                if (root.TryGetProperty("result", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (r.TryGetProperty("player_list", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        count = pl.GetArrayLength();
                        if (count > 0 && pl[0].TryGetProperty("game_id", out var g)) first = g.GetString() ?? "";
                    }
                }

                _output.WriteLine($"[{label}] status={status} msg=\"{msg}\" 命中={count} 首条={first}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] ERR {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string StripPrefix(string roleId)
        {
            var digitsStart = 0;
            while (digitsStart < roleId.Length && !char.IsAsciiDigit(roleId[digitsStart])) digitsStart++;
            return digitsStart >= roleId.Length ? roleId : roleId[digitsStart..].TrimStart('0');
        }
    }
}
