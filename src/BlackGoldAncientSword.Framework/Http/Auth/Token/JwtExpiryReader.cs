using System;
using System.Text;
using System.Text.Json;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 从 JWT access token 里解出 <c>exp</c>（秒）并换算成 Unix 毫秒。
    /// 不做签名校验——签名校验是服务端职责，客户端只关心过期时间用于决定是否走 refresh。
    /// </summary>
    public static class JwtExpiryReader
    {
        public static long ReadExpiresAtUnixMs(string? accessToken)
        {
            if (string.IsNullOrEmpty(accessToken)) return 0;

            var parts = accessToken.Split('.');
            if (parts.Length < 2) return 0;

            try
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);
                if (!doc.RootElement.TryGetProperty("exp", out var expEl)) return 0;
                if (!expEl.TryGetInt64(out var expSec)) return 0;
                return expSec * 1000L;
            }
            catch
            {
                return 0;
            }
        }

        private static byte[] Base64UrlDecode(string s)
        {
            var padded = s.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
