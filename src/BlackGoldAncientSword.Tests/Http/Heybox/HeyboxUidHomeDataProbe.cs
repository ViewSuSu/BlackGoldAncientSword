using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxUidHomeDataProbe
    {
        private readonly ITestOutputHelper _output;

        public HeyboxUidHomeDataProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_HomeData_For_Given_Uid()
        {
            var raw = Environment.GetEnvironmentVariable("HEYBOX_SESSION");
            Assert.False(string.IsNullOrWhiteSpace(raw), "需要环境变量 HEYBOX_SESSION（测试凭证），本探针不读本地存档");
            using var doc = JsonDocument.Parse(raw!);
            var session = new HeyboxSession(
                doc.RootElement.GetProperty("heybox_id").GetString() ?? string.Empty,
                doc.RootElement.GetProperty("pkey").GetString() ?? string.Empty);
            HeyboxLiveSetup.ConfigureClient(session);

            var uids = (Environment.GetEnvironmentVariable("HEYBOX_PROBE_UIDS") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            Assert.NotEmpty(uids);

            foreach (var uid in uids)
            {
                await Probe(uid);
                await Task.Delay(HeyboxLiveSetup.IntervalMilliseconds);
            }
        }

        [Fact]
        public async Task Probe_MatchDetail_TeammateNames()
        {
            var raw = Environment.GetEnvironmentVariable("HEYBOX_SESSION");
            Assert.False(string.IsNullOrWhiteSpace(raw), "需要环境变量 HEYBOX_SESSION（测试凭证）");
            using var doc = JsonDocument.Parse(raw!);
            var session = new HeyboxSession(
                doc.RootElement.GetProperty("heybox_id").GetString() ?? string.Empty,
                doc.RootElement.GetProperty("pkey").GetString() ?? string.Empty);
            HeyboxLiveSetup.ConfigureClient(session);

            var roleId = Environment.GetEnvironmentVariable("HEYBOX_PROBE_ROLE_ID");
            Assert.False(string.IsNullOrWhiteSpace(roleId), "需要环境变量 HEYBOX_PROBE_ROLE_ID");

            var list = await NarakaApiClient.Http
                .GetStringAsync($"/game/yjwj/match/list?role_id={Uri.EscapeDataString(roleId!)}&server=163&limit=3&offset=0")
                .ConfigureAwait(false);
            using var listDoc = JsonDocument.Parse(list);
            var matchId = listDoc.RootElement.GetProperty("result").GetProperty("match_list")[0].GetProperty("match_id").GetString();
            _output.WriteLine($"最近一局 match_id = {matchId}");

            await Task.Delay(HeyboxLiveSetup.IntervalMilliseconds);

            var detail = await NarakaApiClient.Http
                .GetStringAsync($"/game/yjwj/match/detail?match_id={Uri.EscapeDataString(matchId!)}")
                .ConfigureAwait(false);
            using var detailDoc = JsonDocument.Parse(detail);
            var root = detailDoc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            _output.WriteLine($"详情 status={status} 字节={detail.Length}");

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return;

            _output.WriteLine("result 字段一览：");
            foreach (var prop in result.EnumerateObject())
            {
                var kind = prop.Value.ValueKind;
                var size = kind == JsonValueKind.Array ? $" 长度={prop.Value.GetArrayLength()}" : string.Empty;
                var sample = kind == JsonValueKind.String ? $" 值=\"{prop.Value.GetString()}\"" : string.Empty;
                _output.WriteLine($"    {prop.Name}: {kind}{size}{sample}");

                if (kind != JsonValueKind.Array || prop.Value.GetArrayLength() == 0) continue;
                var first = prop.Value[0];
                if (first.ValueKind != JsonValueKind.Object) continue;
                var keys = string.Join(", ", first.EnumerateObject().Select(p => p.Name));
                _output.WriteLine($"        [0] 字段: {keys}");
                var nameLike = first.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String
                                && (p.Name.Contains("name", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("uid", StringComparison.OrdinalIgnoreCase)))
                    .Select(p => $"{p.Name}=\"{p.Value.GetString()}\"");
                _output.WriteLine($"        [0] 名字类字段: {string.Join("  ", nameLike)}");
            }
        }

        private async Task Probe(string uid)
        {
            var path = $"/game/yjwj/home/data?role_id={Uri.EscapeDataString(uid)}&server=163&season=qianji&battle_tid=5000001";
            try
            {
                var body = await NarakaApiClient.Http.GetStringAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : null;
                var hasResult = root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object;
                var hasPlayerInfo = false;
                string? name = null;
                if (hasResult && result.TryGetProperty("player_info", out var pi) && pi.ValueKind == JsonValueKind.Object)
                {
                    hasPlayerInfo = true;
                    if (pi.TryGetProperty("name", out var n)) name = n.GetString();
                }

                var resp = JsonSerializer.Deserialize<HeyboxHomeResponse>(body, NarakaApiClient.JsonOptions);
                var info = UnifiedMapper.MapPlayerInfo(resp?.Result);
                var stats = UnifiedMapper.MapSeasonSummary(resp?.Result);

                _output.WriteLine(
                    $"{uid}  status={status}  msg=\"{msg}\"  result={hasResult}  player_info={(hasPlayerInfo ? "有" : "无")}  " +
                    $"昵称=\"{name}\"  统计={(stats?.Stats?.Count ?? 0)}项  MapPlayerInfo={(info is null ? "NULL" : "ok")}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{uid}  ERR {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
