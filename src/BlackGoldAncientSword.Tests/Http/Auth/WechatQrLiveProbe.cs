using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    /// <summary>
    /// 一次性 live probe：验证新域名 desktop.naraka.drivod.top + appId=naraka-desktop 是否能走通
    /// "取签名票据 → 走签名 → 打业务接口"。不做业务断言，只把 HTTP 状态和响应体打印出来供人判断。
    /// 玩家查询一律用本机活跃账号的角色 ID：与主程序同一套现有逻辑（PlayerPrefsService）动态获取，
    /// 不写死测试账号，拿不到 UID 直接失败，不回退名字搜索；数据源用 search 返回的 source，不写死 dashen。
    /// 通过 Collection 与其它 Live 测试串行，避免全局 NarakaApiClient.Configure 互相覆盖。
    /// </summary>
    [Trait("Category", "Live")]
    [Collection(UnifiedLiveCollection.Name)]
    public class WechatQrLiveProbe
    {
        private readonly ITestOutputHelper _output;
        public WechatQrLiveProbe(ITestOutputHelper output) => _output = output;

        private static bool _configured;

        [Fact]
        public async Task Probe_SignedPipeline_AllEndpoints()
        {
            EnsureSignedAndAuthenticated();

            var ticketProvider = new SignatureTicketProvider();
            var ticket = await ticketProvider.GetAsync(CancellationToken.None);
            _output.WriteLine($"[ticket] appId={ticket.AppId}  secretLen={ticket.AppSecret?.Length ?? 0}  expireInMs={ticket.ExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            _output.WriteLine("");

            var heyBox = DataSource.HeyBox.ToApiString();

            await Probe("GetGameModes (公开)", async ct =>
            {
                var resp = await NarakaApiClient.GetGameModesAsync(heyBox, ct);
                return $"code={resp.Code} msg=\"{resp.Msg}\" dataCount={resp.Data?.Count ?? 0}";
            });
            await Task.Delay(1200);

            // 本机 UID：与主程序同一套现有逻辑（player_prefs 的 player_id）动态获取，不写死；
            // 拿不到直接失败，不回退名字搜索。
            var roleIdSimple = await GetLocalRoleIdSimpleAsync();

            await Probe("SearchRecord by local UID (公开)", async ct =>
            {
                var resp = await NarakaApiClient.SearchRecordAsync(roleIdSimple, null, ct);
                return $"code={resp.Code} msg=\"{resp.Msg}\" roleIdSimple={resp.Data?.RoleIdSimple} src={resp.Data?.Source}";
            });
            await Task.Delay(1200);

            // 数据源用 search 返回的 source（unified 域实际为 heyBox），不写死 dashen。
            var source = await ResolveLocalSourceAsync(roleIdSimple);

            await Probe("GetPlayerProfile (需 Bearer)", async ct =>
            {
                var resp = await NarakaApiClient.GetPlayerProfileAsync(source, roleIdSimple, ct);
                return $"code={resp.Code} msg=\"{resp.Msg}\" roleName={resp.Data?.DisplayName}";
            });
            await Task.Delay(1200);

            // 微信扫码创建：不带 captchaVerification 预期业务级 400。走 NarakaApiClient.Http（同一签名链路）直发裸 http。
            _output.WriteLine("[WechatQrCreate (需滑块)]");
            using (var res = await NarakaApiClient.Http.PostAsJsonAsync(
                "/app-api/member/auth/wechat-mp/qr-code",
                new { captchaVerification = "" },
                CancellationToken.None))
            {
                var body = await res.Content.ReadAsStringAsync();
                _output.WriteLine($"  HTTP {(int)res.StatusCode} body={body}");
            }
        }

        /// <summary>挂 SignatureHandler + AuthTokenHandler 链，并从本地 auth.dat 恢复 token 到 state。只配置一次。</summary>
        private static void EnsureSignedAndAuthenticated()
        {
            if (_configured) return;
            var ticketProvider = new SignatureTicketProvider();
            var store = new DpapiAuthTokenStore();
            var token = store.Load();
            if (token == null)
                throw new InvalidOperationException("本地无有效 token，请先在 App 里登录（%APPDATA%\\BlackGoldAncientSword\\auth.dat）");

            var state = new AuthTokenState();
            state.Set(token);
            var refresher = new AuthTokenRefresher(ticketProvider);
            var challenge = new Mock<IAuthChallengeService>().Object;
            var handler = new SignatureHandler(ticketProvider)
            {
                InnerHandler = new AuthTokenHandler(state, store, refresher, challenge)
                {
                    InnerHandler = new HttpClientHandler()
                }
            };
            NarakaApiClient.Configure(handler);
            _configured = true;
        }

        /// <summary>复用主程序现有逻辑动态获取本机活跃账号角色 ID；拿不到直接失败，不回退名字。</summary>
        private static async Task<string> GetLocalRoleIdSimpleAsync()
        {
            var svc = new PlayerPrefsService();
            await svc.LoadAsync().ConfigureAwait(false);
            var id = svc.Current.PlayerId;
            Assert.False(
                string.IsNullOrWhiteSpace(id),
                "本机拿不到活跃账号角色 ID（player_prefs/Player.log），必须用本地 UID 测试，无法回退名字");
            return id!;
        }

        /// <summary>用本机 UID 搜 unified 拿到实际数据源；拿不到直接失败。</summary>
        private static async Task<string> ResolveLocalSourceAsync(string roleIdSimple)
        {
            var resp = await NarakaApiClient.SearchRecordAsync(roleIdSimple, null, CancellationToken.None).ConfigureAwait(false);
            var search = UnifiedMapper.MapSearch(resp);
            Assert.NotNull(search);
            return search!.DataSource.ToApiString();
        }

        private async Task Probe(string name, Func<CancellationToken, Task<string>> call)
        {
            _output.WriteLine($"[{name}]");
            try
            {
                var summary = await call(CancellationToken.None);
                _output.WriteLine($"  OK  {summary}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ERR {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
