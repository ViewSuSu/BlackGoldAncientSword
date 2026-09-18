using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class TeamCardQueryProbe
    {
        private sealed class StubTextProvider : ILocalizedTextProvider
        {
            public string Get(string key, string fallback) => fallback;
        }

        private readonly ITestOutputHelper _output;

        public TeamCardQueryProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_TeamCard_Paths()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            var prefs = new BlackGoldAncientSword.GameMonitor.Services.Implementation.PlayerPrefsService();
            await prefs.LoadAsync();
            var roleId = prefs.Current.PlayerId;
            var name = prefs.Current.OriginalPlayerName;

            var stats = new BlackGoldAncientSword.Modules.UI.TeamInfo.Services.PlayerStatsLoader(
                new StubTextProvider(), new HeyboxHomeDataProvider(new HeyboxRequestCache()));
            var loader = new TeamMemberLoader(stats, new HeyboxHomeDataProvider(new HeyboxRequestCache()), new HeyboxRequestCache());

            _output.WriteLine($"本机 role_id={roleId}  name={name}");

            await Run(loader, "队友卡形状(userName=UID, uidOverride=UID)", roleId, roleId);
            await Run(loader, "本地卡形状(userName=昵称, uidOverride=UID)", name, roleId);
            await Run(loader, "去掉 UID 覆盖(userName=昵称, uidOverride=null)", name, null);
        }

        private async Task Run(TeamMemberLoader loader, string label, string userName, string? uidOverride)
        {
            try
            {
                var r = await loader.LoadAsync(userName, null, GameModeCategory.Rank, TeamSize.Trio,
                    CancellationToken.None, uidOverride).ConfigureAwait(false);

                _output.WriteLine($"[{label}] Failed={r.Failed} FailMsg=\"{r.FailMsg}\" " +
                                  $"name=\"{r.UserName}\" level=\"{r.Level}\" uid=\"{r.UID}\" " +
                                  $"stats={(r.Stats is null ? "null" : r.Stats.Stats.Count.ToString())}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] ERR {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
