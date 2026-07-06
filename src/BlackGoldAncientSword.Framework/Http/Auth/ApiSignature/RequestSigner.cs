using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    /// <summary>
    /// 与网页 <c>naraka-h5</c> 侧的 <c>P7</c> 函数严格对齐的请求签名器。
    /// <para>
    /// 算法（<c>sha256Hex</c> 均为小写 hex）：
    /// <code>
    /// paramHash = sha256Hex(paramCount + ":" + sortedParams)
    /// bodyHash  = sha256Hex(bodyLen    + ":" + rawBody)
    /// signHeadersStr = "appId=&lt;x&gt;&amp;nonce=&lt;y&gt;&amp;timestamp=&lt;z&gt;"      // 字典序，不 URL encode
    /// parts = [
    ///   "NRK2",                          // 前缀魔数
    ///   METHOD.toUpper(),
    ///   String(timestamp.length),        // 13
    ///   nonce.slice(0, 8),
    ///   paramHash.slice(8, 40),          // 32 chars
    ///   bodyHash.slice(0, 32),           // 32 chars
    ///   signHeadersStr,
    ///   reverse(appSecret),
    /// ].join("|")
    /// payload = parts + "::drivod:naraka:api:v2::" + appSecret.slice(0, 12)
    /// sign    = sha256Hex(payload)
    /// </code>
    /// </para>
    /// <para>
    /// params 合并规则：URL query + axios params，URL 优先（先到者胜），值不做 URL encode（decode 后原样参与）。
    /// body 规则：FormData/Blob/ArrayBuffer/null 视为空串；string 原样；其它 <c>JSON.stringify</c>。
    /// </para>
    /// </summary>
    public static class RequestSigner
    {
        private const string SignPrefix = "NRK2";
        private const string SignSuffix = "::drivod:naraka:api:v2::";

        /// <summary>
        /// 计算指定请求 + ticket 下的签名，并直接把 4 个签名头写回 <paramref name="request"/>。
        /// 若同名头已存在会先移除避免重复。
        /// </summary>
        /// <param name="timestampMs">Unix 毫秒时间戳；单元测试注入固定值，运行时传 <c>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()</c>。</param>
        /// <param name="nonce">12 字节随机 hex（24 字符）；单元测试注入固定值，运行时用 <see cref="RandomNumberGenerator"/> 生成。</param>
        public static async Task SignAsync(
            HttpRequestMessage request,
            SignatureTicket ticket,
            long timestampMs,
            string nonce,
            CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (ticket is null) throw new ArgumentNullException(nameof(ticket));
            if (nonce is null) throw new ArgumentNullException(nameof(nonce));

            var timestamp = timestampMs.ToString(CultureInfo.InvariantCulture);

            var rawBody = await ReadRawBodyAsync(request, ct).ConfigureAwait(false);
            var mergedParams = MergeParams(request.RequestUri);
            var sortedParams = BuildSortedKv(mergedParams);

            var signHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                [SignatureConstants.HeaderAppId] = ticket.AppId,
                [SignatureConstants.HeaderNonce] = nonce,
                [SignatureConstants.HeaderTimestamp] = timestamp,
            };
            var signHeadersStr = string.Join("&", signHeaders.Select(kv => $"{kv.Key}={kv.Value}"));

            var paramHash = ComputeSha256Hex($"{mergedParams.Count}:{sortedParams}");
            var bodyHash = ComputeSha256Hex($"{rawBody.Length}:{rawBody}");

            var method = (request.Method?.Method ?? "GET").ToUpperInvariant();
            var appSecret = ticket.AppSecret ?? string.Empty;

            var parts = new[]
            {
                SignPrefix,
                method,
                timestamp.Length.ToString(CultureInfo.InvariantCulture),
                SafeSlice(nonce, 0, 8),
                SafeSlice(paramHash, 8, 32),
                SafeSlice(bodyHash, 0, 32),
                signHeadersStr,
                Reverse(appSecret),
            };
            var payload = string.Join("|", parts) + SignSuffix + SafeSlice(appSecret, 0, 12);
            var sign = ComputeSha256Hex(payload);

            SetHeader(request.Headers, SignatureConstants.HeaderAppId, ticket.AppId);
            SetHeader(request.Headers, SignatureConstants.HeaderTimestamp, timestamp);
            SetHeader(request.Headers, SignatureConstants.HeaderNonce, nonce);
            SetHeader(request.Headers, SignatureConstants.HeaderSign, sign);
        }

        public static string GenerateNonce()
        {
            Span<byte> buffer = stackalloc byte[12];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToHexString(buffer).ToLowerInvariant();
        }

        /// <summary>
        /// 合并 URL query 到字典。与 JS 侧 <c>H7 + U7</c> 一致：
        /// URL query 先入，重复 key 保留先到者，值 URL-decode 后参与签名。
        /// </summary>
        internal static Dictionary<string, string> MergeParams(Uri? uri)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (uri is null) return dict;

            string query;
            if (uri.IsAbsoluteUri)
            {
                query = uri.Query;
            }
            else
            {
                var raw = uri.OriginalString;
                var qIdx = raw.IndexOf('?');
                query = qIdx < 0 ? string.Empty : raw.Substring(qIdx);
            }

            if (query.Length > 0 && query[0] == '?') query = query.Substring(1);
            if (query.Length == 0) return dict;

            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair.Substring(0, eq);
                var value = eq < 0 ? string.Empty : pair.Substring(eq + 1);
                if (dict.ContainsKey(key)) continue;
                dict[key] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return dict;
        }

        internal static string BuildSortedKv(Dictionary<string, string> dict) =>
            string.Join("&", dict.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        internal static async Task<string> ReadRawBodyAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = request.Content;
            if (content is null) return string.Empty;

            // 与 JS G7 对齐：FormData / Blob / ArrayBuffer / null 返回空串
            if (content is MultipartContent) return string.Empty;
            if (content is ByteArrayContent && content is not StringContent) return string.Empty;

            var text = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return text ?? string.Empty;
        }

        internal static string ComputeSha256Hex(string payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string SafeSlice(string s, int start, int len)
        {
            if (string.IsNullOrEmpty(s) || start >= s.Length) return string.Empty;
            var take = Math.Min(len, s.Length - start);
            return s.Substring(start, take);
        }

        private static string Reverse(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var arr = s.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }

        private static void SetHeader(HttpRequestHeaders headers, string name, string value)
        {
            if (headers.Contains(name)) headers.Remove(name);
            headers.TryAddWithoutValidation(name, value);
        }
    }
}
