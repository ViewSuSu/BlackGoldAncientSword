using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using Newtonsoft.Json;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class GitHubReleaseService : IGitHubReleaseService
    {
        private const string ReleasesUrl = "https://api.github.com/repos/ViewSuSu/BlackGoldAncientSword/releases";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static GitHubReleaseService()
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BlackGoldAncientSword");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<List<GitHubReleaseInfo>> GetReleasesAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(ReleasesUrl);
                var rawList = JsonConvert.DeserializeObject<List<GitHubReleaseRaw>>(json);
                if (rawList == null) return new List<GitHubReleaseInfo>();

                var result = new List<GitHubReleaseInfo>(rawList.Count);
                foreach (var r in rawList)
                {
                    result.Add(new GitHubReleaseInfo
                    {
                        TagName = r.TagName ?? string.Empty,
                        Name = r.Name ?? string.Empty,
                        Body = r.Body ?? string.Empty,
                        PublishedAt = r.PublishedAt ?? string.Empty,
                        HtmlUrl = r.HtmlUrl ?? string.Empty
                    });
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GitHubReleaseService] 获取 releases 失败: {ex.Message}");
                return new List<GitHubReleaseInfo>();
            }
        }

        [JsonObject]
        private class GitHubReleaseRaw
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }

            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("body")]
            public string? Body { get; set; }

            [JsonProperty("published_at")]
            public string? PublishedAt { get; set; }

            [JsonProperty("html_url")]
            public string? HtmlUrl { get; set; }
        }
    }
}