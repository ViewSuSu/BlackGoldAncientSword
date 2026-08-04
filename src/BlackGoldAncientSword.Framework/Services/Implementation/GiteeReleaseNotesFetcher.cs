using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 从 Gitee release 页面 endpoint 拉 tag 描述。
    ///
    /// 设计要点：GET `https://gitee.com/{owner}/{repo}/releases/tag/{tag}` 且 UA 非浏览器时，
    /// Gitee 返回 JSON（release.description 即 body），并非 `/api/v5/*` 路径，
    /// 不受未鉴权 IP 的 60 req/min 限流，与 UpdateService 的"零 API 依赖"策略一致。
    ///
    /// 未来 Gitee 若改成必返 HTML，此实现返回 null，UI 侧 HasReleaseNotes 变 false 自动隐藏，不崩。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class GiteeReleaseNotesFetcher : IReleaseNotesFetcher
    {
        private const string GiteeOwner = "SususuChang";
        private const string GiteeRepo = "BlackGoldAncientSword";
        private const string TagPageBase =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases/tag/";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly HttpClient _http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");
            // Gitee 会根据 Accept 头决定 releases/tag/{tag} 返回 JSON 还是 HTML；
            // .NET HttpClient 默认不发 Accept，Gitee 视作浏览器返 HTML（内嵌 SPA 骨架，
            // release body 由 JS 后填充，抓不到）。显式声明 JSON 才能拿到 release.description。
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        public async Task<string?> FetchAsync(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var normalized = NormalizeTag(tag);
            var url = TagPageBase + normalized;

            try
            {
                using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[{nameof(GiteeReleaseNotesFetcher)}] {url} 返回 {(int)resp.StatusCode}");
                    return null;
                }

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseDescription(body);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                AppLog.Error(ex, nameof(GiteeReleaseNotesFetcher), $"拉取失败 {url}");
                return null;
            }
        }

        /// <summary>
        /// 若响应为 JSON（Gitee 在非浏览器 UA 下的默认行为），解析 release.description；
        /// 否则返 null。分离该方法便于单测走 fixture 断言。
        /// </summary>
        internal static string? ParseDescription(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;

            var trimmed = payload.AsSpan().TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{') return null;

            try
            {
                var root = JsonSerializer.Deserialize<GiteeTagPageEnvelope>(payload, _jsonOptions);
                var desc = root?.Release?.Release?.Description;
                return string.IsNullOrWhiteSpace(desc) ? null : desc;
            }
            catch (JsonException ex)
            {
                AppLog.Error(ex, nameof(GiteeReleaseNotesFetcher), "JSON 解析失败");
                return null;
            }
        }

        private static string NormalizeTag(string tag)
        {
            var t = tag.Trim();
            if (t.Length == 0) return t;
            return (t[0] == 'v' || t[0] == 'V') ? t : "v" + t;
        }

        private class GiteeTagPageEnvelope
        {
            [JsonPropertyName("release")]
            public GiteeReleaseOuter? Release { get; set; }
        }

        private class GiteeReleaseOuter
        {
            [JsonPropertyName("release")]
            public GiteeReleaseInner? Release { get; set; }
        }

        private class GiteeReleaseInner
        {
            [JsonPropertyName("description")]
            public string? Description { get; set; }
        }
    }
}
