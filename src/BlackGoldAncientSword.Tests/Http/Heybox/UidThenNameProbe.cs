using System;
using System.Linq;
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
    public class UidThenNameProbe
    {
        private readonly ITestOutputHelper _output;

        public UidThenNameProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_Uid_To_Name_To_Search()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            var prefs = new BlackGoldAncientSword.GameMonitor.Services.Implementation.PlayerPrefsService();
            await prefs.LoadAsync();

            var cases = new[]
            {
                ("本机", prefs.Current.PlayerId),
                ("别人 A", "1o51000145498000010163"),
                ("通用昵称", "jhc8000039740000080163"),
            };

            foreach (var (label, uid) in cases)
            {
                await Run(label, uid);
            }
        }

        private async Task Run(string label, string uid)
        {
            try
            {
                var raw = await NarakaApiClient.Http
                    .GetStringAsync($"/game/yjwj/home/data?role_id={Uri.EscapeDataString(uid)}&server=163")
                    .ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("result", out var r)
                    && r.TryGetProperty("matches", out var ms)
                    && ms.ValueKind == System.Text.Json.JsonValueKind.Array
                    && ms.GetArrayLength() > 0
                    && ms[0].TryGetProperty("scene", out var sc))
                {
                    _output.WriteLine($"[{label}] 原始 matches[0].scene = {sc.GetRawText()} (ValueKind={sc.ValueKind})");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] 原始抓取 ERR {ex.GetType().Name}: {ex.Message}");
            }

            string? name = null;
            try
            {
                var home = await new HeyboxHomeDataProvider(new HeyboxRequestCache())
                    .GetAsync(uid, "163", season: null, battleTid: null, CancellationToken.None).ConfigureAwait(false);
                name = home?.Result?.PlayerInfo?.Name;
                var serverDesc = home?.Result?.ServerDesc;
                _output.WriteLine($"[{label}] ① home/data?role_id={uid} → status={home?.Status} 昵称=\"{name}\" server_desc=\"{serverDesc}\"");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] ① ERR {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (string.IsNullOrWhiteSpace(name)) { _output.WriteLine($"[{label}] ① 没拿到昵称，② 跳过"); return; }

            try
            {
                var resp = await NarakaApiClient.SearchPlayersAsync(
                    gameType: "yjwj", q: name, offset: 0, limit: 20, ct: CancellationToken.None).ConfigureAwait(false);

                var players = resp?.Result?.PlayerList ?? new();
                var hit = players.FirstOrDefault(p => string.Equals(p.GameId, uid, StringComparison.OrdinalIgnoreCase));

                _output.WriteLine($"[{label}] ② search?q=\"{name}\" → status={resp?.Status} 返回={players.Count} 条" +
                                  (hit is null
                                      ? "  ❌ 我们的目标不在这一页里"
                                      : $"  ✅ 命中 第{players.IndexOf(hit) + 1}条 ext={hit.Ext}"));

                if (players.Count > 0)
                    _output.WriteLine($"[{label}]    前 5 条 game_id: " +
                                      string.Join(", ", players.Take(5).Select(p => p.GameId)));
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] ② ERR {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
