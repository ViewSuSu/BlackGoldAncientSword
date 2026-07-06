using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Auth;
using BlackGoldAncientSword.Framework.Http.Auth.Token;

namespace BlackGoldAncientSword.Framework.Http.Auth.WechatQr
{
    public sealed record QrChallenge(string Scene, string VerificationCode, string QrCodeUrl, int PollIntervalMs);

    public enum QrPollOutcome { WaitingScan, Scanned, Success, Expired, Failed }

    public sealed record QrPollResult(QrPollOutcome Outcome, AuthToken? Token);

    public interface IWechatQrLoginService
    {
        Task<QrChallenge?> CreateAsync(string captchaVerification, CancellationToken ct);
        Task<QrPollResult> PollAsync(string scene, CancellationToken ct);
        Task CancelAsync(string scene, CancellationToken ct);
    }

    [Component(ComponentLifetime.Singleton)]
    public sealed class WechatQrLoginService : IWechatQrLoginService
    {
        private readonly HttpClient _http;

        public WechatQrLoginService(ISignedOnlyHttpClient signed) => _http = signed.Client;

        public async Task<QrChallenge?> CreateAsync(string captchaVerification, CancellationToken ct)
        {
            using var res = await _http.PostAsJsonAsync("/app-api/member/auth/wechat-mp/qr-code",
                new CreateReq { CaptchaVerification = captchaVerification }, cancellationToken: ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<Envelope<CreateData>>(cancellationToken: ct).ConfigureAwait(false);
            if (body is null || (body.Code != 0 && body.Code != 200) || body.Data is null) return null;
            var d = body.Data;
            if (string.IsNullOrEmpty(d.Scene)) return null;
            var qrUrl = d.QrCodeUrl ?? string.Empty;
            // 相对路径转绝对：网页里 axios 用 baseURL=/app-api，返回的 qrCodeUrl 可能是同源相对路径
            if (!string.IsNullOrEmpty(qrUrl) && !qrUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !qrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var sep = qrUrl.StartsWith("/") ? string.Empty : "/";
                qrUrl = "https://naraka.drivod.top" + sep + qrUrl;
            }
            return new QrChallenge(d.Scene, d.VerificationCode ?? string.Empty, qrUrl, d.PollIntervalMillis > 0 ? d.PollIntervalMillis : 1500);
        }

        public async Task<QrPollResult> PollAsync(string scene, CancellationToken ct)
        {
            using var res = await _http.GetAsync("/app-api/member/auth/wechat-mp/status?scene=" + Uri.EscapeDataString(scene), ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new QrPollResult(QrPollOutcome.Failed, null);
            var body = await res.Content.ReadFromJsonAsync<Envelope<StatusData>>(cancellationToken: ct).ConfigureAwait(false);
            if (body is null) return new QrPollResult(QrPollOutcome.Failed, null);
            if (body.Code != 0 && body.Code != 200) return new QrPollResult(QrPollOutcome.Failed, null);

            // 与网页 j() 对齐：status 是大写 WAITING / SUCCESS；成功时 token 在 data.login 里
            var status = (body.Data?.Status ?? "WAITING").ToUpperInvariant();
            switch (status)
            {
                case "SUCCESS":
                case "LOGGED":
                {
                    var login = body.Data?.Login;
                    var access = login?.AccessToken ?? login?.Token;
                    if (string.IsNullOrEmpty(access)) return new QrPollResult(QrPollOutcome.Failed, null);
                    var refresh = login?.RefreshToken ?? string.Empty;
                    var userJson = login?.User is null ? null : JsonSerializer.Serialize(login.User);
                    var token = new AuthToken(access, refresh, userJson, JwtExpiryReader.ReadExpiresAtUnixMs(access));
                    return new QrPollResult(QrPollOutcome.Success, token);
                }
                case "SCANNED":
                case "SCAN":
                    return new QrPollResult(QrPollOutcome.Scanned, null);
                case "EXPIRED":
                    return new QrPollResult(QrPollOutcome.Expired, null);
                default:
                    return new QrPollResult(QrPollOutcome.WaitingScan, null);
            }
        }

        public async Task CancelAsync(string scene, CancellationToken ct)
        {
            try
            {
                using var _ = await _http.PostAsJsonAsync("/app-api/member/auth/wechat-mp/cancel",
                    new { scene }, cancellationToken: ct).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }

        private sealed class Envelope<T> { [JsonPropertyName("code")] public int Code { get; set; } [JsonPropertyName("msg")] public string? Msg { get; set; } [JsonPropertyName("data")] public T? Data { get; set; } }
        private sealed class CreateReq { [JsonPropertyName("captchaVerification")] public string CaptchaVerification { get; set; } = ""; }
        private sealed class CreateData
        {
            [JsonPropertyName("scene")] public string? Scene { get; set; }
            [JsonPropertyName("verificationCode")] public string? VerificationCode { get; set; }
            [JsonPropertyName("qrCodeUrl")] public string? QrCodeUrl { get; set; }
            [JsonPropertyName("pollIntervalMillis")] public int PollIntervalMillis { get; set; }
        }
        private sealed class StatusData
        {
            [JsonPropertyName("status")] public string? Status { get; set; }
            [JsonPropertyName("login")] public LoginPayload? Login { get; set; }
        }

        private sealed class LoginPayload
        {
            [JsonPropertyName("token")] public string? Token { get; set; }
            [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
            [JsonPropertyName("refreshToken")] public string? RefreshToken { get; set; }
            [JsonPropertyName("user")] public JsonElement? User { get; set; }
        }
    }
}
