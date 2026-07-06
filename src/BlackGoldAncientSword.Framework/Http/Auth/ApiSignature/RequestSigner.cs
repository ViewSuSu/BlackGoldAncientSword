using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    /// <summary>
    /// 与网页 <c>D7</c> 函数对齐的请求签名器。
    /// <para>
    /// <c>sign = SHA256Hex( sortedQuery + rawBody + sortedSignHeaders + appSecret )</c>
    /// </para>
    /// <para>
    /// 其中 sorted 均为按 key 字典序、<c>key=value</c> 用 <c>&amp;</c> 拼接（无 URL encode，与 JS 侧一致）。
    /// signHeaders = <c>{appId, nonce, timestamp}</c>。
    /// </para>
    /// </summary>
    public static class RequestSigner
    {
        /// <summary>
        /// 计算指定请求 + ticket 下的签名，并直接把 4 个签名头写回 <paramref name="request"/>。
        /// 若同名头已存在会先移除避免重复。
        /// </summary>
        /// <param name="timestampMs">Unix 毫秒时间戳；单元测试注入固定值，运行时传 <c>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()</c>。</param>
        /// <param name="nonce">12 字节随机 hex；单元测试注入固定值，运行时用 <see cref="RandomNumberGenerator"/> 生成。</param>
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

            var timestamp = timestampMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var rawBody = await ReadRawBodyAsync(request, ct).ConfigureAwait(false);
            var sortedQuery = BuildSortedQuery(request.RequestUri);
            var signHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                [SignatureConstants.HeaderAppId] = ticket.AppId,
                [SignatureConstants.HeaderNonce] = nonce,
                [SignatureConstants.HeaderTimestamp] = timestamp,
            };
            var sortedSignHeaders = string.Join("&", signHeaders.Select(kv => $"{kv.Key}={kv.Value}"));

            var payload = sortedQuery + rawBody + sortedSignHeaders + ticket.AppSecret;
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

        internal static string BuildSortedQuery(Uri? uri)
        {
            if (uri is null) return string.Empty;

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

            if (string.IsNullOrEmpty(query)) return string.Empty;
            if (query.Length > 0 && query[0] == '?') query = query.Substring(1);
            if (query.Length == 0) return string.Empty;

            var parsed = HttpUtility.ParseQueryString(query);
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string? key in parsed.AllKeys)
            {
                if (key is null) continue;
                if (sorted.ContainsKey(key)) continue;
                var value = parsed[key] ?? string.Empty;
                sorted[key] = value;
            }

            return string.Join("&", sorted.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        internal static async Task<string> ReadRawBodyAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = request.Content;
            if (content is null) return string.Empty;

            // 与 JS F7 对齐：FormData / Blob / ArrayBuffer / null 返回空串
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

        private static void SetHeader(HttpRequestHeaders headers, string name, string value)
        {
            if (headers.Contains(name)) headers.Remove(name);
            headers.TryAddWithoutValidation(name, value);
        }
    }
}
