using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IGitHubReleaseService
    {
        Task<List<GitHubReleaseInfo>> GetReleasesAsync();
    }

    public class GitHubReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string PublishedAt { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
    }
}