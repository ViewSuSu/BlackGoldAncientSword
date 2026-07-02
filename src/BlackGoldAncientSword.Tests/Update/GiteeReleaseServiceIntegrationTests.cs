using System.Net.Http;
using BlackGoldAncientSword.Framework.Services.Implementation;

namespace BlackGoldAncientSword.Tests.Update;

/// <summary>
/// 集成测试：验证 GiteeReleaseService 能从真实 Gitee API 拉取 release 列表，
/// 字段（TagName / Name / Body / PublishedAt / HtmlUrl）被正确映射。
/// 网络不可用时跳过。
/// </summary>
public class GiteeReleaseServiceIntegrationTests
{
    private const string ExpectedReleasePagePrefix =
        "https://gitee.com/SususuChang/BlackGoldAncientSword/releases/tag/";

    private static async Task<bool> IsGiteeReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await http.GetAsync("https://gitee.com/api/v5/repos/SususuChang/BlackGoldAncientSword");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task GetReleasesAsync_ReturnsNonEmptyList()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = new GiteeReleaseService();
        var releases = await svc.GetReleasesAsync();

        Assert.NotNull(releases);
        Assert.NotEmpty(releases);
    }

    [Fact]
    public async Task GetReleasesAsync_MapsAllFieldsCorrectly()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = new GiteeReleaseService();
        var releases = await svc.GetReleasesAsync();

        Assert.NotEmpty(releases);

        foreach (var r in releases)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.TagName), "TagName 不应为空");
            Assert.False(string.IsNullOrWhiteSpace(r.Name), "Name 不应为空");
            Assert.False(string.IsNullOrWhiteSpace(r.PublishedAt), "PublishedAt 不应为空");
            Assert.True(
                DateTime.TryParse(r.PublishedAt, out _),
                $"PublishedAt 应可解析为 DateTime，实际: {r.PublishedAt}");

            Assert.Equal(ExpectedReleasePagePrefix + r.TagName, r.HtmlUrl);
        }
    }

    [Fact]
    public async Task GetReleasesAsync_LatestTagLooksLikeVersion()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = new GiteeReleaseService();
        var releases = await svc.GetReleasesAsync();

        Assert.NotEmpty(releases);

        var latest = releases[0];
        var tag = latest.TagName;
        var normalized = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;

        Assert.True(
            Version.TryParse(normalized, out _),
            $"最新 TagName 应为 v{{version}} 或 {{version}} 格式，实际: {tag}");
    }
}
