using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Auth;

namespace BlackGoldAncientSword.Framework.Http.Auth.Captcha
{
    public sealed record CaptchaChallenge(
        string OriginalImageBase64,
        string JigsawImageBase64,
        string Token,
        string? SecretKey);

    public interface IAjCaptchaService
    {
        /// <summary>
        /// 拉一次滑块拼图。返回背景图 + 滑块图 + token（+ secretKey，非空则说明后端要求 AES 加密 pointJson）。
        /// </summary>
        Task<CaptchaChallenge?> GetAsync(CancellationToken ct);

        /// <summary>
        /// 提交滑块位置 x（相对参考宽度 <see cref="ReferenceImageWidth"/> 归一化后的坐标，浮点两位）。
        /// 成功返回 captchaVerification 供下一步 wechat-mp/qr-code 使用；失败返回 null。
        /// </summary>
        Task<string?> CheckAsync(CaptchaChallenge challenge, double x, CancellationToken ct);

        /// <summary>参考图宽度：网页 R9=310。</summary>
        int ReferenceImageWidth { get; }

        /// <summary>y 坐标常量：网页 gte=5。</summary>
        int ReferenceY { get; }
    }

    [Component(ComponentLifetime.Singleton)]
    public sealed class AjCaptchaService : IAjCaptchaService
    {
        private const string CaptchaType = "blockPuzzle";
        private const string SuccessRepCode = "0000";

        private readonly HttpClient _http;

        public int ReferenceImageWidth => 310;
        public int ReferenceY => 5;

        public AjCaptchaService(ISignedOnlyHttpClient signed) => _http = signed.Client;

        public async Task<CaptchaChallenge?> GetAsync(CancellationToken ct)
        {
            using var res = await _http.PostAsJsonAsync("/app-api/system/captcha/get",
                new GetReq { CaptchaType = CaptchaType }, cancellationToken: ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            // 服务端返回是**扁平** {repCode, repMsg, repData} —— 不套通用 {code,msg,data} 包
            var body = await res.Content.ReadFromJsonAsync<CaptchaRepEnvelope>(cancellationToken: ct).ConfigureAwait(false);
            if (body is null || body.RepCode != SuccessRepCode || body.RepData is null) return null;
            var d = body.RepData;
            if (string.IsNullOrEmpty(d.OriginalImageBase64) || string.IsNullOrEmpty(d.JigsawImageBase64) || string.IsNullOrEmpty(d.Token))
                return null;
            return new CaptchaChallenge(d.OriginalImageBase64, d.JigsawImageBase64, d.Token, string.IsNullOrEmpty(d.SecretKey) ? null : d.SecretKey);
        }

        public async Task<string?> CheckAsync(CaptchaChallenge challenge, double x, CancellationToken ct)
        {
            var pointJsonPlain = JsonSerializer.Serialize(new { x = Math.Round(x, 2), y = ReferenceY });
            var pointJson = string.IsNullOrEmpty(challenge.SecretKey)
                ? pointJsonPlain
                : AesEcbCipher.EncryptToBase64(pointJsonPlain, challenge.SecretKey!);

            using var res = await _http.PostAsJsonAsync("/app-api/system/captcha/check",
                new CheckReq { CaptchaType = CaptchaType, PointJson = pointJson, Token = challenge.Token },
                cancellationToken: ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<CheckRepEnvelope>(cancellationToken: ct).ConfigureAwait(false);
            if (body is null || body.RepCode != SuccessRepCode) return null;

            var raw = $"{challenge.Token}---{pointJsonPlain}";
            return string.IsNullOrEmpty(challenge.SecretKey)
                ? raw
                : AesEcbCipher.EncryptToBase64(raw, challenge.SecretKey!);
        }

        private sealed class GetReq { [JsonPropertyName("captchaType")] public string CaptchaType { get; set; } = ""; }
        private sealed class CheckReq
        {
            [JsonPropertyName("captchaType")] public string CaptchaType { get; set; } = "";
            [JsonPropertyName("pointJson")] public string PointJson { get; set; } = "";
            [JsonPropertyName("token")] public string Token { get; set; } = "";
        }

        private sealed class CaptchaRepEnvelope
        {
            [JsonPropertyName("repCode")] public string? RepCode { get; set; }
            [JsonPropertyName("repMsg")] public string? RepMsg { get; set; }
            [JsonPropertyName("repData")] public CaptchaRep? RepData { get; set; }
        }
        private sealed class CaptchaRep
        {
            [JsonPropertyName("originalImageBase64")] public string? OriginalImageBase64 { get; set; }
            [JsonPropertyName("jigsawImageBase64")] public string? JigsawImageBase64 { get; set; }
            [JsonPropertyName("token")] public string? Token { get; set; }
            [JsonPropertyName("secretKey")] public string? SecretKey { get; set; }
        }
        private sealed class CheckRepEnvelope
        {
            [JsonPropertyName("repCode")] public string? RepCode { get; set; }
            [JsonPropertyName("repMsg")] public string? RepMsg { get; set; }
        }
    }
}
