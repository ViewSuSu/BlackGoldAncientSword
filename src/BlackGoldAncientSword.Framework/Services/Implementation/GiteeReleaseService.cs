using System;
using System.Collections.Generic;
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
    /// 从 Gitee releases 列表页拉取发布历史。
    ///
    /// 设计要点：与 <see cref="GiteeReleaseNotesFetcher"/> 一致，GET 网页路径 `releases`
    /// （非 `/api/v5/*`），带非浏览器 UA + Accept: application/json 时 Gitee 返回 JSON
    /// `{releases:[...]}`，不受未鉴权 IP 的 60 req/min 限流。
    /// 原实现走 `/api/v5/repos/{owner}/{repo}/releases`，实测长期 403 (Rate Limit Exceeded)，
    /// 导致"更新记录"页列表为空。此处改为零 API 依赖策略。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class GiteeReleaseService : IGiteeReleaseService
    {
        private const string GiteeOwner = "SususuChang";
        private const string GiteeRepo = "BlackGoldAncientSword";
        private const string ReleasesUrl =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases";
        private const string ReleasePageBase =
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
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BlackGoldAncientSword");
            return client;
        }

        public async Task<List<GiteeReleaseInfo>> GetReleasesAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(ReleasesUrl).ConfigureAwait(false);
                var envelope = JsonSerializer.Deserialize<GiteeReleasesEnvelope>(json, _jsonOptions);
                var rawList = envelope?.Releases;
                if (rawList == null) return new List<GiteeReleaseInfo>();

                var result = new List<GiteeReleaseInfo>(rawList.Count);
                foreach (var r in rawList)
                {
                    var tag = r.Tag?.Name ?? string.Empty;
                    result.Add(new GiteeReleaseInfo
                    {
                        TagName = tag,
                        Name = r.Release?.Title ?? string.Empty,
                        Body = r.Release?.Description ?? string.Empty,
                        PublishedAt = r.Release?.CreatedAt ?? string.Empty,
                        HtmlUrl = string.IsNullOrEmpty(tag) ? string.Empty : ReleasePageBase + tag
                    });
                }
                return result;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(GiteeReleaseService), "获取 Gitee releases 失败");
                return new List<GiteeReleaseInfo>();
            }
        }

        private class GiteeReleasesEnvelope
        {
            [JsonPropertyName("releases")]
            public List<GiteeReleaseItem>? Releases { get; set; }
        }

        private class GiteeReleaseItem
        {
            [JsonPropertyName("tag")]
            public GiteeTagInfo? Tag { get; set; }

            [JsonPropertyName("release")]
            public GiteeReleaseDetail? Release { get; set; }
        }

        private class GiteeTagInfo
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private class GiteeReleaseDetail
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("created_at")]
            public string? CreatedAt { get; set; }
        }
    }
}
