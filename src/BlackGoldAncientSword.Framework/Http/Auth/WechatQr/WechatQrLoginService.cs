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
                qrUrl = "https://desktop.naraka.drivod.top" + sep + qrUrl;
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
                    // yudao 微信 QR 登录：nickname / avatar / userId 平铺在 login 顶层（Vue 端 auth.userInfo = data.login）。
                    // 只挑客户端 UI 关心的字段序列化成小 JSON，避免把敏感 token 也塞进 UserJson。
                    var userJson = BuildUserJson(login);
                    // yudao AuthLoginRespVO 里 expiresTime 是 LocalDateTime，TimestampLocalDateTimeSerializer
                    // 默认输出 Long Unix ms。opaque token 无 JWT payload，必须优先取服务器字段；
                    // 服务器缺失时才回退 JwtExpiryReader，避免解析失败导致 0 让本地过期检查恒真。
                    var expiresAt = login?.ExpiresTime > 0 ? login.ExpiresTime : JwtExpiryReader.ReadExpiresAtUnixMs(access);
                    var token = new AuthToken(access, refresh, userJson, expiresAt);
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

        /// <summary>
        /// 从 yudao <c>login</c> 顶层字段挑 userId/username/nickname/avatar 序列化。
        /// UserJson 供 <c>UserProfileViewModel</c> 显示头像/昵称；不放 token/refreshToken 避免误暴露。
        /// </summary>
        private static string? BuildUserJson(LoginPayload? login)
        {
            if (login is null) return null;
            var obj = new
            {
                userId = login.UserId,
                username = login.Username,
                nickname = login.Nickname,
                avatar = login.Avatar,
            };
            return JsonSerializer.Serialize(obj);
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
            [JsonPropertyName("expiresTime")] public long ExpiresTime { get; set; }
            // yudao AuthLoginRespVO + Member 扩展：nickname / avatar / username / userId 均在顶层，不是 user 子对象
            [JsonPropertyName("userId")] public long UserId { get; set; }
            [JsonPropertyName("username")] public string? Username { get; set; }
            [JsonPropertyName("nickname")] public string? Nickname { get; set; }
            [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        }
    }
}
