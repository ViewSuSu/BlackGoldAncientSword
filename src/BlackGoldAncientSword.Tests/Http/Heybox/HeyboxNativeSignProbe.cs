using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxNativeSignProbe
    {
        private const string GameType = "yjwj";

        private const string WebOsType = "webinapp";

        private const string ProbeGameId = "bdhqzm_probe_nonexistent_9f3a2b";

        private readonly ITestOutputHelper _output;

        public HeyboxNativeSignProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Sign_Reproduces_Captured_Request()
        {
            var hkey = HeyboxNativeHkey.Compute(
                "/account/unbind_game_id/",
                epochSeconds: 1790236335,
                deviceId: "e85644968984b1cf",
                userId: "99365688");

            Assert.Equal("CF9E086", hkey);
        }

        [Theory]
        [InlineData("/account/unbind_game_id/", 1758600000, "aabbccddeeff00112233445566778899", "30745242", "4C5A774")]
        [InlineData("/account/bind_game_id/", 1758600001, "test-device", "12345", "2F1AF943")]
        [InlineData("account/unbind_game_id", 0, "D", "U", "B8BAB22B")]
        [InlineData("/account/unbind_game_id", 1758600002, "x", "y", "BE33F438")]
        [InlineData("/a/b/", 1, "dev", "usr", "9C10BB50")]
        [InlineData("/account/unbind_game_id/", 2000000000, "中文设备", "u", "4E54EFEF")]
        public void Sign_Matches_Reference_Vectors(
            string path, long epochSeconds, string deviceId, string userId, string expected)
        {
            Assert.Equal(expected, HeyboxNativeHkey.Compute(path, epochSeconds, deviceId, userId));
        }

        [Fact]
        public void Profile_Defaults_Match_Captured_Request()
        {
            var profile = new HeyboxNativeClientProfile();

            Assert.Equal("V1916A", profile.DeviceInfo);
            Assert.Equal("9", profile.OsVersion);
            Assert.Equal("1.3.391", profile.Version);
            Assert.Equal("1112", profile.Build);
            Assert.Equal("360", profile.Dw);
            Assert.Equal("heybox_xiaomi", profile.Channel);
            Assert.Equal("Asia/Shanghai", profile.TimeZone);
            Assert.Equal("Android", profile.OsType);
            Assert.Equal("mobile", profile.XClientType);
            Assert.Equal(16, profile.Imei.Length);
        }

        [Fact]
        public void Nonce_And_Round_Token_Shapes()
        {
            var nonce = HeyboxNativeHkey.NewNonce();
            Assert.Equal(32, nonce.Length);
            Assert.All(nonce, ch => Assert.True(char.IsAsciiLetterOrDigit(ch), $"非法字符 {ch}"));

            var token = HeyboxNativeHkey.NewRoundToken();
            Assert.StartsWith("14:", token);
            Assert.Equal(11, token.Length);
        }

        [Fact]
        public async Task Probe_Write_Endpoints_With_Local_Session()
        {
            var session = HeyboxLiveSetup.LoadSession();
            Assert.NotNull(session);

            var profile = new HeyboxNativeClientProfile();
            var capture = ConfigureNative(session!, profile);

            _output.WriteLine($"heybox_id={session!.HeyboxId}  imei={profile.Imei}  version={profile.Version}");
            _output.WriteLine($"探测用 game_id=\"{ProbeGameId}\"（不存在，避免动到真实绑定）");
            _output.WriteLine("");

            await ReportAsync("解绑 · App 参数集 + native hkey", capture, async () =>
            {
                var response = await NarakaApiClient.UnbindGameIdAsync(
                    gameType: GameType, gameId: ProbeGameId, osType: WebOsType, ct: CancellationToken.None);
                return (response?.Status, response?.Msg, response?.Result?.State);
            });

            await ReportAsync("绑定 · App 参数集 + native hkey", capture, async () =>
            {
                var response = await NarakaApiClient.BindGameIdAsync(
                    gameType: GameType, gameId: ProbeGameId, osType: WebOsType, serverId: 163, ct: CancellationToken.None);
                return (response?.Status, response?.Msg, response?.Result?.State);
            });

            _output.WriteLine("对照参考：网页签名链下这两个端点的返回分别是「请重新登录」与「请重新登录」。");
        }

        private static CapturingHandler ConfigureNative(HeyboxSession session, HeyboxNativeClientProfile profile)
        {
            var state = new HeyboxSessionState();
            state.Set(HeyboxLoginState.FromSession(session));

            var capture = new CapturingHandler { InnerHandler = new HttpClientHandler { UseCookies = false } };
            NarakaApiClient.Configure(new HeyboxRequestPacingHandler
            {
                IntervalMilliseconds = HeyboxLiveSetup.IntervalMilliseconds,
                InnerHandler = new HeyboxSignatureHandler(state, profile) { InnerHandler = capture },
            });

            return capture;
        }

        private async Task ReportAsync(
            string label, CapturingHandler capture, Func<Task<(string? Status, string? Msg, string? State)>> call)
        {
            try
            {
                var (status, msg, state) = await call().ConfigureAwait(false);
                _output.WriteLine($"[{label}] status={status} msg=\"{msg}\" state={state}");
            }
            catch (NarakaApiException ex)
            {
                _output.WriteLine($"[{label}] 后端拒绝 msg=\"{ex.Msg}\"");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{label}] 异常 {ex.GetType().Name}: {ex.Message}");
            }

            _output.WriteLine($"    实际请求: {capture.LastUrl}");
            _output.WriteLine("");
        }

        private sealed class CapturingHandler : DelegatingHandler
        {
            public string? LastUrl { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUrl = request.RequestUri?.ToString();
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
