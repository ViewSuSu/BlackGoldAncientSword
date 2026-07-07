using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Auth.MemberProfile
{
    /// <summary>
    /// 借用 <see cref="ISignedOnlyHttpClient"/>（只挂 Signature）+ 手动附 Bearer 完成一次性 profile 拉取。
    /// 不用 <c>NarakaApiClient</c>/<c>AuthTokenHandler</c>：那条链会从 <c>IAuthTokenState.Current</c> 读 Bearer，
    /// 但调用方（QR 登录成功后 / refresh 完成后）此刻 Current 未必是本次调用要用的 token；
    /// 显式传参最不容易踩时序坑。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class MemberProfileService : IMemberProfileService
    {
        private readonly HttpClient _signedClient;

        public MemberProfileService(ISignedOnlyHttpClient signed) => _signedClient = signed.Client;

        public async Task<string?> GetProfileJsonAsync(string accessToken, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(accessToken)) return null;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "/app-api/member/user/get");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var res = await _signedClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                var envelope = await res.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct).ConfigureAwait(false);
                if (envelope is null) return null;
                if (envelope.Code != 0 && envelope.Code != 200) return null;
                var d = envelope.Data;
                if (d is null) return null;

                // 只挑 UI 需要的字段序列化，与 WechatQrLoginService.BuildUserJson 输出结构对齐。
                var obj = new
                {
                    userId = d.Id,
                    username = d.Nickname,
                    nickname = d.Nickname,
                    avatar = d.Avatar,
                };
                return JsonSerializer.Serialize(obj);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(MemberProfileService)}.{nameof(GetProfileJsonAsync)}] {ex.Message}");
                return null;
            }
        }

        private sealed class Envelope
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("msg")] public string? Msg { get; set; }
            [JsonPropertyName("data")] public ProfileData? Data { get; set; }
        }

        /// <summary>
        /// yudao <c>/app-api/member/user/get</c> 响应结构（截取 UI 需要的字段）：
        /// <c>{ id, nickname, avatar, groupId, groupName, mobile, email, sex, point, experience, level, brokerageEnabled }</c>。
        /// </summary>
        private sealed class ProfileData
        {
            [JsonPropertyName("id")] public long Id { get; set; }
            [JsonPropertyName("nickname")] public string? Nickname { get; set; }
            [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        }
    }
}
