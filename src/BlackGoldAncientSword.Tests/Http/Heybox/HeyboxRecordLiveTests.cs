using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxRecordLiveTests
    {
        private const string ApiBase = "https://api.xiaoheihe.cn";
        private const string RecordPath = "/game/yjwj/home/data";
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly string[] RateLimitKeywords =
            { "过于频繁", "频繁", "稍后再试", "繁忙", "限流", "操作过快" };

        private readonly ITestOutputHelper _output;

        public HeyboxRecordLiveTests(ITestOutputHelper output) => _output = output;

        private sealed class RequiresHeyboxSessionFactAttribute : FactAttribute
        {
            public RequiresHeyboxSessionFactAttribute()
            {
                if (HasLoginState()) return;

                Skip = $"没有小黑盒登录态：先跑一次 {nameof(HeyboxWechatQrLiveProbe)} 微信扫码登录，"
                     + "也可直接设置环境变量 HEYBOX_SESSION。";
            }
        }

        [RequiresHeyboxSessionFact]
        public async Task Live_HomeData_ContractNotDrifted()
        {
            var session = LoadSession();
            if (session is null)
            {
                _output.WriteLine("没有可用登录态，本次未做任何验证。");
                return;
            }

            var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = HeyboxHkey.NewNonce();
            var hkey = HeyboxHkey.Compute(RecordPath, unixSeconds, nonce);

            var query =
                "app=heybox&os_type=web&x_app=heybox&x_client_type=weboutapp&x_os_type=Windows" +
                "&x_client_version=&version=999.0.4" +
                $"&hkey={hkey}&_time={unixSeconds}&nonce={nonce}&server=163" +
                $"&heybox_id={Uri.EscapeDataString(session.HeyboxId)}";

            _output.WriteLine($"hkey={hkey}  _time={unixSeconds}  heybox_id={Mask(session.HeyboxId)}  pkeyLen={session.Pkey.Length}");

            using var http = new HttpClient(new HttpClientHandler { UseCookies = false })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}{RecordPath}?{query}");
            req.Headers.TryAddWithoutValidation("Cookie", $"user_heybox_id={session.HeyboxId}; user_pkey={session.Pkey}");
            req.Headers.TryAddWithoutValidation("Referer", "https://web.xiaoheihe.cn/");
            req.Headers.TryAddWithoutValidation("Origin", "https://web.xiaoheihe.cn");

            await HeyboxRequestPacing.WaitAsync(HeyboxLiveSetup.IntervalMilliseconds, CancellationToken.None).ConfigureAwait(false);
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            var parsed = TryParseJson(body, out var root);
            var isObject = parsed && root.ValueKind == JsonValueKind.Object;
            JsonElement statusElement = default;
            JsonElement msgElement = default;
            var hasStatus = isObject && root.TryGetProperty("status", out statusElement);
            var hasMessage = isObject && root.TryGetProperty("msg", out msgElement);
            var status = hasStatus ? ReadAsString(statusElement) : null;
            var message = hasMessage ? ReadAsString(msgElement) : null;

            if (IsRateLimited(message) || IsRateLimited(body))
            {
                _output.WriteLine("═══ 账号/IP 被风控，本次契约验证【没有完成】 ═══");
                _output.WriteLine($"msg=「{message}」  HTTP {(int)res.StatusCode}");
                _output.WriteLine("这不是客户端实现问题，等一段时间或换账号再跑。");
                return;
            }

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.True(parsed, $"{RecordPath} 返回的不是 JSON（HTTP {(int)res.StatusCode}）：{Redact(Truncate(body, 300), session)}");
            Assert.True(isObject, $"{RecordPath} 返回的 JSON 根不是对象：{Redact(Truncate(body, 300), session)}");
            Assert.True(hasStatus, $"响应里没有 status 字段：{Redact(Truncate(body, 300), session)}");

            if (status == "ok")
            {
                Assert.True(root.TryGetProperty("result", out _), "status=ok 却没有 result 字段，契约漂移了。");
                _output.WriteLine("status=ok，result 字段在 → 登录态与参数集都有效。");
                return;
            }

            if (status == "relogin")
            {
                _output.WriteLine("═══ status=relogin：会话已失效，本次契约验证【没有完成】 ═══");
                _output.WriteLine("重新登录一次再跑。v2 没有运行时跳过，所以这里不判红——别把绿当成验过了。");
                return;
            }

            if (status == "login")
            {
                Assert.Fail(
                    "带了登录态却返回 status=login：请求没被认成登录态。按顺序核——① Cookie 是否是 user_heybox_id + user_pkey（不能是 heybox_id/pkey）；" +
                    "② query 里 x_client_type=weboutapp / x_client_version=空串 / server=163 是否被改回前端默认值；" +
                    $"③ hkey 是否用本次 path + _time + nonce 现算（先跑 {nameof(HeyboxHkeyTests)} 的黄金向量确认算法没变）；④ 登录态本身是否已过期，重新登录再试。");
            }

            if (status == "failed")
            {
                Assert.Fail($"status=failed，msg=「{message}」：参数集漂移或接口改版，对照 skill 第二节逐项核参数。");
            }

            Assert.Fail($"未知 status「{status}」：{Redact(Truncate(body, 300), session)}");
        }

        private static bool HasLoginState()
        {
            try
            {
                var raw = Environment.GetEnvironmentVariable("HEYBOX_SESSION");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("heybox_id", out var id) && !string.IsNullOrEmpty(id.GetString())
                        && root.TryGetProperty("pkey", out var pkey) && !string.IsNullOrEmpty(pkey.GetString()))
                    {
                        return true;
                    }
                }

                return new DpapiHeyboxSessionStore().Load() is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseJson(string body, out JsonElement root)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                root = doc.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                root = default;
                return false;
            }
        }

        private HeyboxSession? LoadSession()
        {
            var raw = Environment.GetEnvironmentVariable("HEYBOX_SESSION");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("heybox_id", out var id) && root.TryGetProperty("pkey", out var pkey))
                    {
                        _output.WriteLine("登录态来自环境变量 HEYBOX_SESSION");
                        return new HeyboxSession(id.GetString() ?? string.Empty, pkey.GetString() ?? string.Empty);
                    }

                    _output.WriteLine("HEYBOX_SESSION 里缺 heybox_id / pkey，改从本地存档取。");
                }
                catch (JsonException)
                {
                    _output.WriteLine("HEYBOX_SESSION 不是合法 JSON，改从本地存档取。");
                }
            }

            var stored = new DpapiHeyboxSessionStore().Load();
            if (stored is not null) _output.WriteLine($"登录态来自本地存档 {DpapiHeyboxSessionStore.DefaultPath}");
            return stored;
        }

        private static string? ReadAsString(JsonElement element) =>
            element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();

        private static bool IsRateLimited(string? text) =>
            text is not null && Array.Exists(RateLimitKeywords, k => text.Contains(k, StringComparison.Ordinal));

        private static string Mask(string value) =>
            value.Length <= 8 ? "***" : value[..4] + new string('*', Math.Min(value.Length - 8, 12)) + value[^4..];

        private static string Redact(string text, HeyboxSession session) =>
            string.IsNullOrEmpty(session.HeyboxId) ? text : text.Replace(session.HeyboxId, Mask(session.HeyboxId));

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + $"…(+{value.Length - max})";
    }
}
