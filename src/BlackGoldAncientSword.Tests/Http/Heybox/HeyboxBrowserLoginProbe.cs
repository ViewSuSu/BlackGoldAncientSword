using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxBrowserLoginProbe
    {
        private readonly ITestOutputHelper _output;

        public HeyboxBrowserLoginProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_BrowserLogin_Then_SaveSession()
        {
            _output.WriteLine("即将唤起浏览器打开小黑盒登录页，请在页面里完成登录（最多等 5 分钟）。");

            var session = await new HeyboxBrowserLoginService()
                .LoginAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.NotNull(session);
            Assert.False(string.IsNullOrEmpty(session!.HeyboxId), "回调里没有 heybox_id");
            Assert.False(string.IsNullOrEmpty(session.Pkey), "回调里没有 pkey");

            new DpapiHeyboxSessionStore().Save(session);

            _output.WriteLine($"登录成功：heybox_id={Mask(session.HeyboxId)}  pkeyLen={session.Pkey.Length}");
            _output.WriteLine($"登录态已存到 {DpapiHeyboxSessionStore.DefaultPath}");
        }

        private static string Mask(string value) =>
            value.Length <= 8 ? "***" : value[..4] + new string('*', Math.Min(value.Length - 8, 12)) + value[^4..];
    }
}
