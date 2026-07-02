using System.Net.Http;
using BlackGoldAncientSword.Framework.Services.Implementation;

namespace BlackGoldAncientSword.Tests.Update;

/// <summary>
/// GiteeReleaseNotesFetcher 测试：
/// - ParseDescription 用离线 JSON fixture 覆盖正常 / 空 / 非 JSON / 缺字段等分支，无网络依赖。
/// - FetchAsync 集成部分走真实 Gitee，网络不可达时跳过。核心目标是验证：**不依赖 /api/v5**
///   （不受 60 req/min 限流），且能拿到 tag 描述字符串。
/// </summary>
public class GiteeReleaseNotesFetcherTests
{
    // 覆盖真实响应形状：外层 release.release.description（tag 描述）
    private const string ValidPayload = """
    {
      "release": {
        "tag": { "name": "v1.0.0.1" },
        "release": {
          "title": "Release v1.0.0.1",
          "description": "v1.0.0.1 修复若干问题\n\n- 战斗态残留清理\n- OCR 归一化区域重校准"
        }
      }
    }
    """;

    private const string EmptyDescriptionPayload = """
    { "release": { "release": { "description": "" } } }
    """;

    private const string MissingReleaseInnerPayload = """
    { "release": { "tag": { "name": "v1.0.0.1" } } }
    """;

    [Fact]
    public void ParseDescription_ReturnsDescription_WhenPayloadValid()
    {
        var desc = GiteeReleaseNotesFetcher.ParseDescription(ValidPayload);

        Assert.NotNull(desc);
        Assert.Contains("v1.0.0.1 修复若干问题", desc);
        Assert.Contains("OCR 归一化区域重校准", desc);
    }

    [Fact]
    public void ParseDescription_ReturnsNull_WhenDescriptionEmpty()
    {
        Assert.Null(GiteeReleaseNotesFetcher.ParseDescription(EmptyDescriptionPayload));
    }

    [Fact]
    public void ParseDescription_ReturnsNull_WhenInnerReleaseMissing()
    {
        Assert.Null(GiteeReleaseNotesFetcher.ParseDescription(MissingReleaseInnerPayload));
    }

    [Fact]
    public void ParseDescription_ReturnsNull_WhenPayloadIsHtml()
    {
        // Gitee 未来若在浏览器 UA 下返 HTML 也不能崩，需退化为 null。
        var html = "<!DOCTYPE html><html><body>not json</body></html>";
        Assert.Null(GiteeReleaseNotesFetcher.ParseDescription(html));
    }

    [Fact]
    public void ParseDescription_ReturnsNull_WhenPayloadIsBrokenJson()
    {
        Assert.Null(GiteeReleaseNotesFetcher.ParseDescription("{ this is not valid json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseDescription_ReturnsNull_WhenPayloadNullOrWhitespace(string? payload)
    {
        Assert.Null(GiteeReleaseNotesFetcher.ParseDescription(payload!));
    }

    [Fact]
    public async Task FetchAsync_ReturnsNull_WhenTagBlank()
    {
        var fetcher = new GiteeReleaseNotesFetcher();
        Assert.Null(await fetcher.FetchAsync(""));
        Assert.Null(await fetcher.FetchAsync("   "));
    }

    // -------- 集成测试 --------
    // 走真实 Gitee，验证：
    // 1) 不经 /api/v5 也能拿到 tag 描述（走 releases/tag/{tag} 页面 endpoint）；
    // 2) 拿到的描述与 UI Expander 需要的非空字符串契约一致。
    // 网络不可达时静默跳过，避免离线 CI 误判。

    private const string ProbeHost = "https://gitee.com/SususuChang/BlackGoldAncientSword";

    private static async Task<bool> IsGiteeReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await http.GetAsync(ProbeHost);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task FetchAsync_ReturnsNonEmpty_ForKnownTag_Integration()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var fetcher = new GiteeReleaseNotesFetcher();
        // 已发布的稳定 tag，作为回归锚点
        var notes = await fetcher.FetchAsync("v1.0.0.1");

        Assert.False(string.IsNullOrWhiteSpace(notes), "拉取真实 tag 描述应非空");
    }

    [Fact]
    public async Task FetchAsync_NormalizesTagWithoutVPrefix_Integration()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var fetcher = new GiteeReleaseNotesFetcher();
        var withV = await fetcher.FetchAsync("v1.0.0.1");
        var withoutV = await fetcher.FetchAsync("1.0.0.1");

        Assert.Equal(withV, withoutV);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNull_ForNonexistentTag_Integration()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var fetcher = new GiteeReleaseNotesFetcher();
        var notes = await fetcher.FetchAsync("v99.99.99.99");

        Assert.Null(notes);
    }
}
