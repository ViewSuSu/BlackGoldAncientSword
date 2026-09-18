using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxHomeByRoleIdProbe
    {
        private readonly ITestOutputHelper _output;

        public HeyboxHomeByRoleIdProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_HomeData_Directly_By_RoleId()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            await Probe("别人 role_id + server=163", "uipe000001677200140163", "163");

            await Probe("别人 role_id，不带 server", "uipe000001677200140163", null);

            var prefs = new BlackGoldAncientSword.GameMonitor.Services.Implementation.PlayerPrefsService();
            await prefs.LoadAsync();
            await Probe("本机 role_id + server=163", prefs.Current.PlayerId, "163");

            await Probe("队友形态 UID（语音日志格式）", "jhc8000039740000080163", "163");
            await Probe("队友形态 UID 另一个", "1o51000145498000010163", "163");
        }

        private async Task Probe(string label, string roleId, string? server)
        {
            var url = $"/game/yjwj/home/data?role_id={Uri.EscapeDataString(roleId)}";
            if (server is not null) url += $"&server={Uri.EscapeDataString(server)}";

            try
            {
                var raw = await NarakaApiClient.Http.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() : "?";
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "";

                if (status != "ok")
                {
                    _output.WriteLine($"[{label}] status={status} msg=\"{msg}\"");
                    return;
                }

                var r = root.GetProperty("result");
                var pi = r.TryGetProperty("player_info", out var p) ? p : default;
                var name = pi.ValueKind == JsonValueKind.Object && pi.TryGetProperty("name", out var n) ? n.GetString() : "";
                var level = pi.ValueKind == JsonValueKind.Object && pi.TryGetProperty("level", out var l) ? l.GetString() : "";
                var rating = pi.ValueKind == JsonValueKind.Object && pi.TryGetProperty("rating", out var ra) ? ra.GetString() : "";
                var overview = r.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.Array ? ov.GetArrayLength() : 0;
                var seasons = r.TryGetProperty("seasons", out var se) && se.ValueKind == JsonValueKind.Array ? se.GetArrayLength() : 0;
                var battles = r.TryGetProperty("matches", out var ma) && ma.ValueKind == JsonValueKind.Array ? ma.GetArrayLength() : 0;
                var serverDesc = r.TryGetProperty("server_desc", out var sd) ? sd.GetString() : "";
                var roleIdBack = r.TryGetProperty("role_id", out var rb) ? rb.GetString() : "";

                _output.WriteLine($"[{label}] status=ok  昵称=\"{name}\" 段位=\"{level}\" 积分=\"{rating}\" " +
                                  $"server_desc=\"{serverDesc}\" 回显role_id={roleIdBack} " +
                                  $"overview={overview} seasons={seasons} matches={battles}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] ERR {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
