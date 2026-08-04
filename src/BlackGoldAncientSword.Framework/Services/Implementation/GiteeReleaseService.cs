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
    [Component(ComponentLifetime.Singleton)]
    public class GiteeReleaseService : IGiteeReleaseService
    {
        private const string GiteeOwner = "SususuChang";
        private const string GiteeRepo = "BlackGoldAncientSword";
        private const string ReleasesUrl =
            "https://gitee.com/api/v5/repos/" + GiteeOwner + "/" + GiteeRepo +
            "/releases?page=1&per_page=20&direction=desc";
        private const string ReleasePageBase =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases/tag/";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static GiteeReleaseService()
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BlackGoldAncientSword");
        }

        public async Task<List<GiteeReleaseInfo>> GetReleasesAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(ReleasesUrl);
                var rawList = JsonSerializer.Deserialize<List<GiteeReleaseRaw>>(json, _jsonOptions);
                if (rawList == null) return new List<GiteeReleaseInfo>();

                var result = new List<GiteeReleaseInfo>(rawList.Count);
                foreach (var r in rawList)
                {
                    var tag = r.TagName ?? string.Empty;
                    result.Add(new GiteeReleaseInfo
                    {
                        TagName = tag,
                        Name = r.Name ?? string.Empty,
                        Body = r.Body ?? string.Empty,
                        PublishedAt = r.CreatedAt ?? string.Empty,
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

        private class GiteeReleaseRaw
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("created_at")]
            public string? CreatedAt { get; set; }
        }
    }
}
