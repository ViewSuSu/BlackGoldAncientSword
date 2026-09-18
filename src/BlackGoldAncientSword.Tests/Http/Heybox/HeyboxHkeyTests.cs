using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    public class HeyboxHkeyTests
    {
        private readonly ITestOutputHelper _output;

        public HeyboxHkeyTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Compute_MatchesProductionRequest()
        {
            var actual = HeyboxHkey.Compute(
                pathOrUrl: "/game/yjwj/home/data",
                unixSeconds: 1789636970,
                nonce: "A56916C1B0DA57AD71204DE9B4FD9A49");

            Assert.Equal("22PT390", actual);
        }

        [Fact]
        public void Compute_AlwaysReturnsSevenCharacters()
        {
            for (var i = 0; i < 50; i++)
            {
                var hkey = HeyboxHkey.Compute("/game/yjwj/home/data", 1789636970 + i, HeyboxHkey.NewNonce());
                Assert.Equal(7, hkey.Length);
            }
        }

        [Fact]
        public void Compute_AcceptsFullUrlOrPath()
        {
            const long time = 1789636970;
            const string nonce = "A56916C1B0DA57AD71204DE9B4FD9A49";

            var fromPath = HeyboxHkey.Compute("/game/yjwj/home/data", time, nonce);
            var fromUrl = HeyboxHkey.Compute("https://api.xiaoheihe.cn/game/yjwj/home/data?app=heybox&server=163", time, nonce);

            Assert.Equal(fromPath, fromUrl);
            Assert.Equal("22PT390", fromUrl);
        }

        [Fact]
        public void NewNonce_Is32CharUppercaseHex()
        {
            var nonce = HeyboxHkey.NewNonce();

            _output.WriteLine(nonce);
            Assert.Equal(32, nonce.Length);
            Assert.All(nonce, ch => Assert.True(char.IsAsciiDigit(ch) || (ch >= 'A' && ch <= 'F'), $"非法字符 {ch}"));
        }
    }
}
