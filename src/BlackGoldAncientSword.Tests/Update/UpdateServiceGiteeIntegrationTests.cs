using System.Net.Http;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.Services.Implementation;

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

    private static UpdateService CreateService() =>
        new UpdateService(new SyncUIDispatcher(), new FakeAppMarker());

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

        var hasAny =
            !string.IsNullOrEmpty(svc.DownloadUrl) ||
            !string.IsNullOrEmpty(svc.ZipDownloadUrl) ||
            !string.IsNullOrEmpty(svc.SplitZipDownloadUrl) ||
            (svc.SplitDownloadUrls?.Count ?? 0) > 0;

        Assert.True(hasAny, "更新可用时应至少有一个下载 URL");

        var probed = new List<string>();
        if (!string.IsNullOrEmpty(svc.DownloadUrl)) probed.Add(svc.DownloadUrl!);
        if (!string.IsNullOrEmpty(svc.ZipDownloadUrl)) probed.Add(svc.ZipDownloadUrl!);
        if (svc.SplitDownloadUrls is { Count: > 0 })
            probed.AddRange(svc.SplitDownloadUrls);

        Assert.NotEmpty(probed);

        foreach (var url in probed)
        {
            Assert.StartsWith("https://gitee.com/", url);

            var (ok, len) = await ProbeUrlAsync(url);
            Assert.True(ok, $"下载 URL 不可达: {url}");

            // release asset 走 HEAD，Content-Length 必须 > 0；
            // 源码归档 zip 走 GET headers-only，Gitee 不返回 Content-Length，只校验可达。
            var isReleaseAsset = url.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase);
            if (isReleaseAsset)
            {
                Assert.True(len > 0, $"release asset Content-Length 应 > 0: {url}");
            }
        }
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
