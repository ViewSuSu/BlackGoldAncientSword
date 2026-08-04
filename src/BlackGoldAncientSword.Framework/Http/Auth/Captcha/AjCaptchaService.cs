using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
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
        private const string GetUrl = "/app-api/system/captcha/get";
        private const string CheckUrl = "/app-api/system/captcha/check";

        private readonly HttpClient _http;

        public int ReferenceImageWidth => 310;
        public int ReferenceY => 5;

        public AjCaptchaService(ISignedOnlyHttpClient signed) => _http = signed.Client;

        public async Task<CaptchaChallenge?> GetAsync(CancellationToken ct)
        {
            HttpResponseMessage res;
            try
            {
                res = await _http.PostAsJsonAsync(GetUrl,
                    new GetReq { CaptchaType = CaptchaType }, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
                // 截图"验证码加载失败"最常见的根因就在这里：拉滑块图时网络层挂了（超时/拒连/DNS/TLS）。
                // 收敛到本方法统一记录，附 URL + 分类，开发不必再去上层 catch 拼线索。
                AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(GetAsync)}", $"captcha/get network failure [{ClassifyNetworkError(ex, ct)}] url={GetUrl}: {DescribeException(ex)}");
                return null;
            }

            using (res)
            {
                if (!res.IsSuccessStatusCode)
                {
                    // 拉滑块图 HTTP 失败——用户会看到"验证码加载失败"。记下状态码便于区分是网络/网关问题还是被限流。
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(GetAsync)}", $"captcha/get HTTP {(int)res.StatusCode} {res.StatusCode} url={GetUrl}");
                    return null;
                }
                CaptchaRepEnvelope? body;
                try
                {
                    // 服务端返回是**扁平** {repCode, repMsg, repData} —— 不套通用 {code,msg,data} 包
                    body = await res.Content.ReadFromJsonAsync<CaptchaRepEnvelope>(cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // HTTP 200 但响应体不是预期 JSON（网关返回 HTML 错误页 / 内容被篡改）——反序列化失败也会让用户看到加载失败。
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(GetAsync)}", $"captcha/get response parse failed url={GetUrl}: {DescribeException(ex)}");
                    return null;
                }
                if (body is null || body.RepCode != SuccessRepCode || body.RepData is null)
                {
                    // 后端拒绝拉图（repCode 非 0000）——记下 repCode/repMsg，这是定位"换一张也失败"的关键信息。
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(GetAsync)}", $"captcha/get rejected repCode={body?.RepCode ?? "<null-body>"} repMsg={body?.RepMsg}");
                    return null;
                }
                var d = body.RepData;
                if (string.IsNullOrEmpty(d.OriginalImageBase64) || string.IsNullOrEmpty(d.JigsawImageBase64) || string.IsNullOrEmpty(d.Token))
                {
                    // repCode=0000 但图/token 为空：后端契约异常，前端拿不到可渲染的图。这类"该非空却空"最难排查，必须记。
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(GetAsync)}",
                        $"captcha/get empty payload: originalImg={!string.IsNullOrEmpty(d.OriginalImageBase64)} jigsaw={!string.IsNullOrEmpty(d.JigsawImageBase64)} token={!string.IsNullOrEmpty(d.Token)}");
                    return null;
                }
                return new CaptchaChallenge(d.OriginalImageBase64, d.JigsawImageBase64, d.Token, string.IsNullOrEmpty(d.SecretKey) ? null : d.SecretKey);
            }
        }

        public async Task<string?> CheckAsync(CaptchaChallenge challenge, double x, CancellationToken ct)
        {
            var pointJsonPlain = JsonSerializer.Serialize(new { x = Math.Round(x, 2), y = ReferenceY });
            var pointJson = string.IsNullOrEmpty(challenge.SecretKey)
                ? pointJsonPlain
                : AesEcbCipher.EncryptToBase64(pointJsonPlain, challenge.SecretKey!);

            HttpResponseMessage res;
            try
            {
                res = await _http.PostAsJsonAsync(CheckUrl,
                    new CheckReq { CaptchaType = CaptchaType, PointJson = pointJson, Token = challenge.Token },
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
                AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(CheckAsync)}", $"captcha/check network failure [{ClassifyNetworkError(ex, ct)}] url={CheckUrl}: {DescribeException(ex)}");
                return null;
            }

            using (res)
            {
                if (!res.IsSuccessStatusCode)
                {
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(CheckAsync)}", $"captcha/check HTTP {(int)res.StatusCode} {res.StatusCode} url={CheckUrl}");
                    return null;
                }
                CheckRepEnvelope? body;
                try
                {
                    body = await res.Content.ReadFromJsonAsync<CheckRepEnvelope>(cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(CheckAsync)}", $"captcha/check response parse failed url={CheckUrl}: {DescribeException(ex)}");
                    return null;
                }
                if (body is null || body.RepCode != SuccessRepCode)
                {
                    // 滑块位置校验未过（repCode 非 0000）通常是用户没对准，属预期内失败，用 Warning 记 repCode 便于统计"验证失败率"。
                    AppLog.Warning($"{nameof(AjCaptchaService)}.{nameof(CheckAsync)}", $"captcha/check failed repCode={body?.RepCode ?? "<null-body>"} repMsg={body?.RepMsg}");
                    return null;
                }

                var raw = $"{challenge.Token}---{pointJsonPlain}";
                return string.IsNullOrEmpty(challenge.SecretKey)
                    ? raw
                    : AesEcbCipher.EncryptToBase64(raw, challenge.SecretKey!);
            }
        }

        /// <summary>是否属于"网络层失败"——需要用 Warning 记根因的那类。排除调用方主动取消（ct 触发），那不是故障。</summary>
        private static bool IsNetworkFailure(Exception ex, CancellationToken ct)
        {
            // 调用方主动取消（如用户关弹窗）不算故障，交给上层，不在此吞。
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                return false;
            // HttpRequestException=连接/DNS/TLS 层；TaskCanceled(未取消 ct)=HttpClient 超时；IOException=传输中断。
            return ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is OperationCanceledException
                || ex is System.IO.IOException;
        }

        /// <summary>把网络异常粗分类，便于开发一眼看出是超时还是连不上。</summary>
        private static string ClassifyNetworkError(Exception ex, CancellationToken ct)
        {
            // 传入的 ct 未取消却抛 TaskCanceled → 是 HttpClient.Timeout 到点。
            if (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
                return "timeout";
            if (ex is HttpRequestException)
                return "connect";  // DNS/拒连/TLS 等连接期失败
            if (ex is System.IO.IOException)
                return "io";
            return "network";
        }

        /// <summary>展开异常链取最内层信息——HttpRequestException.Message 常笼统，真正根因在 InnerException（SocketException 等）。</summary>
        private static string DescribeException(Exception ex)
        {
            var inner = ex;
            while (inner.InnerException is not null)
                inner = inner.InnerException;
            return ReferenceEquals(inner, ex)
                ? $"{ex.GetType().Name}: {ex.Message}"
                : $"{ex.GetType().Name}: {ex.Message} -> {inner.GetType().Name}: {inner.Message}";
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
