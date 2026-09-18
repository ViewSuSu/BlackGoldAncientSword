using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{
    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxQrLoginService : IHeyboxQrLoginService
    {
        private const string WechatAppId = "wxced0cbce486f737e";

        private const string HeyboxLoginRedirect = "https://api.xiaoheihe.cn/account/wechat/login_redirect/v2/web_sso/";

        private const string CallbackUrl = "http://127.0.0.1/heybox-login-callback";

        private const int PollIntervalMs = 1500;

        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly Regex UuidPattern = new(@"connect/qrcode/([A-Za-z0-9_\-]{8,})", RegexOptions.Compiled);
        private static readonly Regex ErrCodePattern = new(@"wx_errcode=(\d+)", RegexOptions.Compiled);
        private static readonly Regex WxCodePattern = new(@"window\.wx_code='([^']*)'", RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly HttpClient _noRedirect;

        public HeyboxQrLoginService()
        {
            _http = NewClient(new HttpClientHandler { UseCookies = false }, TimeSpan.FromSeconds(45));

            _noRedirect = NewClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }, TimeSpan.FromSeconds(30));
        }

        private static HttpClient NewClient(HttpClientHandler handler, TimeSpan timeout)
        {
            var http = new HttpClient(handler) { Timeout = timeout };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://open.weixin.qq.com/");
            return http;
        }

        public async Task<HeyboxQrChallenge?> CreateAsync(CancellationToken ct)
        {
            var uuid = await FetchUuidAsync(ct).ConfigureAwait(false);
            if (uuid is null) return null;

            try
            {
                var bytes = await _http.GetByteArrayAsync($"https://open.weixin.qq.com/connect/qrcode/{uuid}", ct)
                    .ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    AppLog.Warning($"{nameof(HeyboxQrLoginService)}.{nameof(CreateAsync)}", $"qrcode image empty for uuid={uuid}");
                    return null;
                }

                return new HeyboxQrChallenge(uuid, bytes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(HeyboxQrLoginService)}.{nameof(CreateAsync)}", "qrcode image download failed");
                return null;
            }
        }

        private async Task<string?> FetchUuidAsync(CancellationToken ct)
        {
            var heyboxRedirect = $"{HeyboxLoginRedirect}?redirect_url={Uri.EscapeDataString(CallbackUrl)}";
            var url = "https://open.weixin.qq.com/connect/qrconnect" +
                      $"?appid={WechatAppId}" +
                      $"&redirect_uri={Uri.EscapeDataString(heyboxRedirect)}" +
                      "&response_type=code&scope=snsapi_login&state=xiaoheihe";

            try
            {
                var html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                var match = UuidPattern.Match(html);
                if (match.Success) return match.Groups[1].Value;

                AppLog.Warning($"{nameof(HeyboxQrLoginService)}.{nameof(FetchUuidAsync)}",
                    $"no qrcode uuid in weixin page ({html.Length} chars)");
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(HeyboxQrLoginService)}.{nameof(FetchUuidAsync)}");
                return null;
            }
        }

        public async Task<HeyboxQrPollResult> PollAsync(string uuid, CancellationToken ct)
        {
            string body;
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                body = await _http.GetStringAsync(
                    $"https://long.open.weixin.qq.com/connect/l/qrconnect?uuid={Uri.EscapeDataString(uuid)}&_={ts}", ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AppLog.Warning($"{nameof(HeyboxQrLoginService)}.{nameof(PollAsync)}", $"long-poll ended: {ex.GetType().Name}");
                return new HeyboxQrPollResult(HeyboxQrOutcome.WaitingScan, null);
            }

            var code = ErrCodePattern.Match(body).Groups[1].Value;
            switch (code)
            {
                case "405":
                {
                    var wxCode = WxCodePattern.Match(body).Groups[1].Value;
                    if (string.IsNullOrEmpty(wxCode))
                    {
                        AppLog.Warning($"{nameof(HeyboxQrLoginService)}.{nameof(PollAsync)}", "wx_errcode=405 but wx_code missing");
                        return new HeyboxQrPollResult(HeyboxQrOutcome.Failed, null);
                    }
                    var session = await ExchangeAsync(wxCode, ct).ConfigureAwait(false);
                    return session is null
                        ? new HeyboxQrPollResult(HeyboxQrOutcome.Failed, null)
                        : new HeyboxQrPollResult(HeyboxQrOutcome.Success, session);
                }
                case "404":
                    return new HeyboxQrPollResult(HeyboxQrOutcome.Scanned, null);
                case "402":
                case "403":
                    return new HeyboxQrPollResult(HeyboxQrOutcome.Expired, null);
                default:
                    return new HeyboxQrPollResult(HeyboxQrOutcome.WaitingScan, null);
            }
        }

        private async Task<HeyboxSession?> ExchangeAsync(string wxCode, CancellationToken ct)
        {
            var url = $"{HeyboxLoginRedirect}?redirect_url={Uri.EscapeDataString(CallbackUrl)}" +
                      $"&code={Uri.EscapeDataString(wxCode)}&state=xiaoheihe";

            try
            {
                using var res = await _noRedirect.GetAsync(url, ct).ConfigureAwait(false);

                var cookies = ReadSetCookies(res);
                var query = ParseQuery(res.Headers.Location?.ToString());

                var heyboxId = FirstNonEmpty(
                    cookies.GetValueOrDefault("user_heybox_id"), cookies.GetValueOrDefault("heybox_id"),
                    query.GetValueOrDefault("heybox_id"));
                var pkey = FirstNonEmpty(
                    cookies.GetValueOrDefault("user_pkey"), cookies.GetValueOrDefault("pkey"),
                    query.GetValueOrDefault("pkey"));

                if (string.IsNullOrEmpty(heyboxId) || string.IsNullOrEmpty(pkey))
                {
                    AppLog.Warning($"{nameof(HeyboxQrLoginService)}.{nameof(ExchangeAsync)}",
                        $"login exchange returned HTTP {(int)res.StatusCode} without heybox_id+pkey ({cookies.Count} cookies)");
                    return null;
                }

                _ = long.TryParse(query.GetValueOrDefault("expire_at"), out var expireAt);
                return new HeyboxSession(heyboxId, pkey, expireAt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(HeyboxQrLoginService)}.{nameof(ExchangeAsync)}");
                return null;
            }
        }

        private static Dictionary<string, string> ReadSetCookies(HttpResponseMessage res)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!res.Headers.TryGetValues("Set-Cookie", out var setCookies)) return result;

            foreach (var raw in setCookies)
            {
                var first = raw.Split(';')[0];
                var eq = first.IndexOf('=');
                if (eq <= 0) continue;
                result[first[..eq].Trim()] = first[(eq + 1)..].Trim();
            }

            return result;
        }

        private static Dictionary<string, string> ParseQuery(string? url)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(url)) return result;

            var idx = url.IndexOf('?');
            if (idx < 0) return result;

            foreach (var pair in url[(idx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }

            return result;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrEmpty(v)) return v;
            return null;
        }
    }
}
