using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 与 JS 侧 <c>X7</c> 对齐：
    /// <c>POST /app-api/member/auth/refresh-token?refreshToken=&lt;&gt;</c>，请求需签名，但**不能**再触发 Bearer/refresh，
    /// 所以走一个只挂了 <see cref="SignatureHandler"/> 的独立 HttpClient。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class AuthTokenRefresher : IAuthTokenRefresher
    {
        private readonly HttpClient _signedClient;

        public AuthTokenRefresher(ISignatureTicketProvider ticketProvider)
        {
            var handler = new SignatureHandler(ticketProvider) { InnerHandler = new HttpClientHandler() };
            _signedClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://desktop.naraka.drivod.top"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            _signedClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            _signedClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }

        // Test constructor
        internal AuthTokenRefresher(HttpClient signedClient) => _signedClient = signedClient;

        public async Task<AuthToken?> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(refreshToken)) return null;

            var path = "/app-api/member/auth/refresh-token?refreshToken=" + Uri.EscapeDataString(refreshToken);
            using var req = new HttpRequestMessage(HttpMethod.Post, path);
            using var res = await _signedClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            var envelope = await res.Content.ReadFromJsonAsync<RefreshEnvelope>(cancellationToken: ct).ConfigureAwait(false);
            if (envelope is null) return null;
            if (envelope.Code != 0 && envelope.Code != 200) return null;
            var data = envelope.Data;
            if (data is null) return null;

            var access = data.AccessToken ?? data.Token;
            if (string.IsNullOrEmpty(access)) return null;

            var newRefresh = data.RefreshToken ?? refreshToken;
            // yudao /auth/refresh-token 响应**不含 profile 字段**（nickname/avatar 只在 /member/user/get 里）。
            // UserJson 留空，由调用方（AuthTokenHandler / AuthTokenExpiryMonitor）用 previous.UserJson 合并保留。
            // 与 QR 登录响应对齐：yudao 返回 expiresTime (Long Unix ms)，opaque token 无法从 JWT 解析。
            var expiresAt = data.ExpiresTime > 0 ? data.ExpiresTime : JwtExpiryReader.ReadExpiresAtUnixMs(access);
            return new AuthToken(access, newRefresh, UserJson: null, expiresAt);
        }

        private sealed class RefreshEnvelope
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("msg")] public string? Msg { get; set; }
            [JsonPropertyName("data")] public RefreshData? Data { get; set; }
        }

        private sealed class RefreshData
        {
            [JsonPropertyName("token")] public string? Token { get; set; }
            [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
            [JsonPropertyName("refreshToken")] public string? RefreshToken { get; set; }
            [JsonPropertyName("expiresTime")] public long ExpiresTime { get; set; }
        }
    }
}
