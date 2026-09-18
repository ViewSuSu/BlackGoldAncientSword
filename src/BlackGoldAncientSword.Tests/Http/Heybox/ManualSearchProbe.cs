using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class ManualSearchProbe
    {
        private readonly ITestOutputHelper _output;

        public ManualSearchProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_Manual_Typed_Search()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);
            HeyboxLiveSetup.ConfigureClient(session!);

            var prefs = new PlayerPrefsService();
            await prefs.LoadAsync();

            var loader = new BlackGoldAncientSword.Modules.UI.Stats.Services.PlayerStatsLoader(
                new HeyboxHomeDataProvider(new HeyboxRequestCache()), new HeyboxRequestCache());

            var inputs = new[]
            {
                prefs.Current.OriginalPlayerName,
                "与我翻阅辞海",
                "空城芷冬",
                prefs.Current.PlayerId,
                "这个名字肯定不存在xyzzy",
            };

            foreach (var input in inputs)
            {
                if (string.IsNullOrWhiteSpace(input)) continue;
                try
                {
                    var r = await loader.SearchLocalPlayerAsync(null, input, CancellationToken.None).ConfigureAwait(false);
                    _output.WriteLine(r is null
                        ? $"[输入 \"{input}\"] → 没搜到"
                        : $"[输入 \"{input}\"] → roleId={r.RoleIdSimple} server={r.Server} 昵称=\"{r.RoleName}\" 段位=\"{r.LevelName}\"");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[输入 \"{input}\"] ERR {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
