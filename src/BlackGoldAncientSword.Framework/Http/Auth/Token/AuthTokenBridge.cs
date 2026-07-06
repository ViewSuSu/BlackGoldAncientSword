using System.Text.Json;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 把 WebView2 内嵌网页 postMessage 回来的 JSON 解析成 <see cref="AuthToken"/>。
    /// 抽成 pure static 就能脱离 WebView2 单测。
    /// <para>
    /// 期望的 message shape：
    /// <code>{ "t": "&lt;access token&gt;", "rt": "&lt;refresh token&gt;", "u": "&lt;user json string, 可选&gt;" }</code>
    /// </para>
    /// </summary>
    public static class AuthTokenBridge
    {
        public static AuthToken? TryParse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("t", out var tEl)) return null;
                var access = tEl.GetString();
                if (string.IsNullOrEmpty(access)) return null;

                var refresh = root.TryGetProperty("rt", out var rtEl) ? rtEl.GetString() ?? string.Empty : string.Empty;
                var user = root.TryGetProperty("u", out var uEl) ? uEl.GetString() : null;

                return new AuthToken(access, refresh, user, JwtExpiryReader.ReadExpiresAtUnixMs(access));
            }
            catch
            {
                return null;
            }
        }
    }
}
