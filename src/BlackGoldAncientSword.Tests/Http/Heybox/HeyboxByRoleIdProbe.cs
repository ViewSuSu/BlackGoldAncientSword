using System;
using System.Linq;
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
    public class HeyboxByRoleIdProbe
    {
        private readonly ITestOutputHelper _output;

        public HeyboxByRoleIdProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_QueryByLocalRoleId()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            var prefs = new PlayerPrefsService();
            await prefs.LoadAsync();
            var roleId = prefs.Current.PlayerId;
            var name = prefs.Current.PlayerName;
            _output.WriteLine($"本机 role_id={roleId}   player_name={name}");
            Assert.False(string.IsNullOrWhiteSpace(roleId), "本机拿不到 role_id");

            var ct = CancellationToken.None;

            try
            {
                var r = await NarakaApiClient.SearchPlayersAsync(gameType: "yjwj", q: roleId, offset: 0, limit: 5, ct: ct);
                var n = r?.Result?.PlayerList?.Count ?? 0;
                _output.WriteLine($"[① 搜索 q=role_id ] status={r?.Status} msg=\"{r?.Msg}\" 命中={n}" +
                                  (n > 0 ? $" 首条={r!.Result!.PlayerList![0].GameId}" : ""));
            }
            catch (Exception ex) { _output.WriteLine($"[① 搜索 q=role_id ] ERR {ex.GetType().Name}: {ex.Message}"); }


            try
            {
                var r = await NarakaApiClient.SearchPlayersAsync(gameType: "yjwj", q: name, offset: 0, limit: 5, ct: ct);
                var list = r?.Result?.PlayerList;
                _output.WriteLine($"[② 搜索 q=昵称 ] status={r?.Status} msg=\"{r?.Msg}\" 命中={list?.Count ?? 0}");
                if (list is not null)
                    foreach (var p in list.Take(3)) _output.WriteLine($"      game_id={p.GameId}");
            }
            catch (Exception ex) { _output.WriteLine($"[② 搜索 q=昵称 ] ERR {ex.GetType().Name}: {ex.Message}"); }


            try
            {
                var r = await NarakaApiClient.GetPlayerHomeAsync(server: "163", roleId: roleId, season: null, battleTid: null, ct: ct);
                _output.WriteLine($"[③ home/data role_id ] status={r?.Status} msg=\"{r?.Msg}\"");
                if (r?.Result is { } d)
                {
                    _output.WriteLine($"      player_info.name={d.PlayerInfo?.Name} seasons={d.Seasons?.Count} " +
                                      $"mode={d.Mode?.Count} overview={d.Overview?.Count} matches={d.Matches?.Count}");
                }
            }
            catch (Exception ex) { _output.WriteLine($"[③ home/data role_id ] ERR {ex.GetType().Name}: {ex.Message}"); }


            string? firstMatch = null;
            try
            {
                var r = await NarakaApiClient.GetMatchListAsync(server: "163", roleId: roleId, limit: 10, offset: 0, ct: ct);
                var list = r?.Result?.MatchList;
                _output.WriteLine($"[④ match/list role_id ] status={r?.Status} msg=\"{r?.Msg}\" 场次={list?.Count ?? 0}");
                if (list is { Count: > 0 }) firstMatch = list[0].MatchId;
            }
            catch (Exception ex) { _output.WriteLine($"[④ match/list role_id ] ERR {ex.GetType().Name}: {ex.Message}"); }

            if (string.IsNullOrEmpty(firstMatch)) return;


            try
            {
                var r = await NarakaApiClient.GetMatchDetailAsync(matchId: firstMatch, ct: ct);
                _output.WriteLine($"[⑤ match/detail 只传 match_id ] status={r?.Status} msg=\"{r?.Msg}\" resultNull={r?.Result is null}");
            }
            catch (Exception ex) { _output.WriteLine($"[⑤ match/detail 只传 match_id ] ERR {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
