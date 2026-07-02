using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IGiteeReleaseService
    {
        Task<List<GiteeReleaseInfo>> GetReleasesAsync();
    }

    public class GiteeReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string PublishedAt { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
    }
}
