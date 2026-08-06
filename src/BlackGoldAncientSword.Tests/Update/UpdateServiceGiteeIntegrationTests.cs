using System.Net.Http;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.Services.Implementation;
using Moq;

namespace BlackGoldAncientSword.Tests.Update;

/// <summary>
/// 集成测试：验证 UpdateService 切换到 Gitee API 后端到端可用：
/// 能拉到最新版本、正确判断是否需要更新、下载 URL 可达。
/// 网络不可用时跳过。
/// </summary>
public class UpdateServiceGiteeIntegrationTests
{
    private const string GiteeApiProbe =
        "https://gitee.com/api/v5/repos/SususuChang/BlackGoldAncientSword";

    private static async Task<bool> IsGiteeReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await http.GetAsync(GiteeApiProbe);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static UpdateService CreateService(IReleaseNotesFetcher? notesFetcher = null) =>
        new UpdateService(
            new SyncUIDispatcher(),
            new FakeAppMarker(),
            notesFetcher ?? new GiteeReleaseNotesFetcher());

    /// <summary>
    /// 探测下载 URL 是否可达。区分两种资产：
    /// - release asset (/releases/download/...)：走 HEAD，Content-Length 应 > 0；
    /// - 源码归档 (/archive/...)：Gitee 动态生成，HEAD 返回 405，改用 GET headers-only 判可达。
    /// </summary>
    private static async Task<(bool ok, long contentLength)> ProbeUrlAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword-Tests");

            var useHead = url.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase);
            var method = useHead ? HttpMethod.Head : HttpMethod.Get;

            using var req = new HttpRequestMessage(method, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

            if (!resp.IsSuccessStatusCode)
                return (false, 0);

            var len = resp.Content.Headers.ContentLength ?? 0;
            return (true, len);
        }
        catch
        {
            return (false, 0);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_PopulatesLatestVersion()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = CreateService();
        await svc.CheckForUpdatesAsync(showNoUpdateMessage: false);

        Assert.False(string.IsNullOrEmpty(svc.CurrentVersion), "CurrentVersion 应有值");

        if (!svc.IsUpdateAvailable)
        {
            Assert.True(true, "已是最新版本，无 LatestVersion 可断言。");
            return;
        }

        Assert.False(string.IsNullOrEmpty(svc.LatestVersion), "LatestVersion 应有值");
        Assert.True(
            Version.TryParse(svc.LatestVersion, out _),
            $"LatestVersion 应为可解析版本号，实际: {svc.LatestVersion}");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ExposesReleaseNotesAndPage()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = CreateService();
        await svc.CheckForUpdatesAsync(showNoUpdateMessage: false);

        Assert.StartsWith(
            "https://gitee.com/SususuChang/BlackGoldAncientSword/releases/",
            svc.ReleasePageUrl);

        if (!svc.IsUpdateAvailable)
        {
            Assert.True(true, "已是最新版本，无 release notes 可断言。");
            return;
        }

        Assert.False(string.IsNullOrEmpty(svc.LatestReleaseNotes), "LatestReleaseNotes 应有值");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ProvidesDownloadableAssets()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = CreateService();
        await svc.CheckForUpdatesAsync(showNoUpdateMessage: false);

        if (!svc.IsUpdateAvailable)
        {
            Assert.True(true, "已是最新版本，跳过资产下载探测。");
            return;
        }

        // DownloadUrl 现语义为 Downloader.exe alias（Gitee 特有 `releases/download/latest/{file}`
        // 伪 tag 语法，302 → attach_files → foruda CDN）。可达性依赖 latest release 是否已上传该附件，
        // 本测试不作可达性断言（附件缺失时 404 属预期）。
        Assert.Equal(
            "https://gitee.com/SususuChang/BlackGoldAncientSword/releases/download/latest/BlackGoldAncientSword-win-x64-Downloader.exe",
            svc.DownloadUrl);

        // ZipDownloadUrl 是直接拼死不做 HEAD 探测，release 未附带全 zip 时 URL 会 404，语义上
        // 只是"若存在则给在线更新用"，可达性不作断言。SplitDownloadUrls 是 HEAD 探测过存在的分卷，
        // 保留可达性断言。
        Assert.True(
            (svc.SplitDownloadUrls?.Count ?? 0) > 0,
            "更新可用时至少应有一组 split zip 分卷可用于在线更新");

        var probed = new List<string>(svc.SplitDownloadUrls!);

        foreach (var url in probed)
        {
            Assert.StartsWith("https://gitee.com/", url);

            var (ok, len) = await ProbeUrlAsync(url);
            Assert.True(ok, $"下载 URL 不可达: {url}");

            var isReleaseAsset = url.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase);
            if (isReleaseAsset)
            {
                Assert.True(len > 0, $"release asset Content-Length 应 > 0: {url}");
            }
        }
    }

    /// <summary>
    /// 验证 UpdateService 把 IReleaseNotesFetcher.FetchAsync 的结果原样赋给 LatestReleaseNotes，
    /// 确保新版本弹窗（UpdateNotificationPage）里 ReleaseNotes/HasReleaseNotes 绑定链路通。
    /// </summary>
    [Fact]
    public async Task CheckForUpdatesAsync_PipesFetcherResult_IntoLatestReleaseNotes()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        const string mockNotes = "MOCK_RELEASE_NOTES\n- line1\n- line2";
        var fetcherMock = new Mock<IReleaseNotesFetcher>();
        fetcherMock
            .Setup(f => f.FetchAsync(It.IsAny<string>()))
            .ReturnsAsync(mockNotes);

        var svc = CreateService(fetcherMock.Object);
        await svc.CheckForUpdatesAsync(showNoUpdateMessage: false);

        if (!svc.IsUpdateAvailable)
        {
            Assert.True(true, "已是最新版本，本机 CurrentVersion 已≥Gitee latest，跳过。");
            return;
        }

        // 新版支持范围拉取：会枚举 current..latest 之间所有 tag 并合并
        Assert.NotNull(svc.LatestReleaseNotes);
        Assert.Contains(mockNotes, svc.LatestReleaseNotes);
        fetcherMock.Verify(f => f.FetchAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SplitAssets_AllHaveSequentialSuffix()
    {
        if (!await IsGiteeReachableAsync())
        {
            Assert.True(true, "Gitee 不可达，跳过。");
            return;
        }

        var svc = CreateService();
        await svc.CheckForUpdatesAsync(showNoUpdateMessage: false);

        if (!svc.IsUpdateAvailable || svc.SplitDownloadUrls is not { Count: > 0 } urls)
        {
            Assert.True(true, "无 split zip 资产，跳过。");
            return;
        }

        foreach (var url in urls)
        {
            Assert.Contains(".zip.", url);
        }
    }

    private sealed class SyncUIDispatcher : IUIDispatcher
    {
        public bool CheckAccess() => true;
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
        public void BeginInvoke(Action action) => action();
    }

    private sealed class FakeAppMarker : IAppAssemblyMarker { }
}
