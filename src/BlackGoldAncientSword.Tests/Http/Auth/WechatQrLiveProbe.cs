using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    /// <summary>
    /// 一次性 live probe：验证新域名 desktop.naraka.drivod.top + appId=naraka-desktop 是否能走通
    /// "取签名票据 → 走签名 → 打业务接口"。不做业务断言，只把 HTTP 状态和响应体打印出来供人判断。
    /// AuthTokenHandler 不挂：本机没登录 token，只想看签名链路的行为。
    /// </summary>
    [Trait("Category", "Live")]
    public class WechatQrLiveProbe
    {
        private readonly ITestOutputHelper _output;
        public WechatQrLiveProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_SignedPipeline_AllEndpoints()
        {
            var ticketProvider = new SignatureTicketProvider();
            var signedOnly = new SignatureHandler(ticketProvider) { InnerHandler = new HttpClientHandler() };

            // 把 NarakaApiClient._http 挂上签名链路（不含 AuthTokenHandler，因为本机无 token）。
            // Configure 是全局静态，一旦调用整个进程内后续所有 NarakaApiClient.* 都走这个链路。
            NarakaApiClient.Configure(signedOnly);

            var ticket = await ticketProvider.GetAsync(CancellationToken.None);
            _output.WriteLine($"[ticket] appId={ticket.AppId}  secretLen={ticket.AppSecret?.Length ?? 0}  expireInMs={ticket.ExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            _output.WriteLine("");

            var daShen = BlackGoldAncientSword.Framework.Core.Consts.DataSource.DaShen.ToApiString();

            await Probe("GetGameModes (公开)", async ct =>
            {
                var resp = await NarakaApiClient.GetGameModesAsync(daShen, ct);
                return $"code={resp.Code} msg=\"{resp.Msg}\" dataCount={resp.Data?.Count ?? 0}";
            });
            await Task.Delay(1200);

            await Probe("SearchRecord \"爱的供养丶\" (公开)", async ct =>
            {
                var resp = await NarakaApiClient.SearchRecordAsync("爱的供养丶", daShen, ct);
                return $"code={resp.Code} msg=\"{resp.Msg}\" roleIdSimple={resp.Data?.RoleIdSimple} src={resp.Data?.Source}";
            });
            await Task.Delay(1200);

            await Probe("GetPlayerProfile (需 Bearer)", async ct =>
            {
                var resp = await NarakaApiClient.GetPlayerProfileAsync(daShen, "15949400120163", ct);
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
