using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 版本检查 + 更新资产解析。
    ///
    /// 设计要点：完全不调 Gitee REST API（/api/v5/...），避免未鉴权 IP 命中
    /// "403 Forbidden (Rate Limit Exceeded)"，与 Downloader 保持一致的"零 API 依赖"策略。
    /// 版本发现：GET `releases/latest` 网页（不 follow redirect），Gitee 会 302 到
    ///   `releases/tag/v{version}`，直接从 Location 头提取 tag，走网页域名不走 API。
    /// 资产 URL：按 CI workflow (dotnet-desktop.yml) 的命名规则拼死，分卷 zip 通过 HEAD
    ///   探测 .001/.002/... 到 404 停，全程走 CDN foruda.gitee.com，不受 API 限流约束。
    /// LatestReleaseNotes：由 <see cref="IReleaseNotesFetcher"/> 从 `releases/tag/{tag}` 非浏览器 UA
    ///   返回的 JSON 中拿 release.description（同域名，非 /api/v5，不受限流）。拉取失败返 null，
    ///   UI 侧 HasReleaseNotes 变 false 自动隐藏更新说明区域。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private const string GiteeOwner = "SususuChang";
        private const string GiteeRepo = "BlackGoldAncientSword";
        private const string GiteeReleaseLatestUrl =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases/latest";
        private const string GiteeDownloadBase =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases/download";

        // Downloader 单文件 exe 的无版本号 alias。
        // Gitee 用伪 tag `latest` 语法 `releases/download/latest/{file}` 永久指向最新 release 的同名附件，
        // 302 → attach_files/... → foruda.gitee.com CDN。与本地版本号无关，随 Gitee latest 变化。
        // 注意：这是 Gitee 特有语法，与 GitHub 的 `releases/latest/download/{file}` 位置相反。
        // 附件名参考 .github/workflows/dotnet-desktop.yml 步骤 "Copy Downloader exe into output"。
        private const string DownloaderAliasName = GiteeRepo + "-win-x64-Downloader.exe";
        private const string DownloaderLatestDownloadUrl =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo +
            "/releases/download/latest/" + DownloaderAliasName;

        // 与 workflow "Create full zip" 步骤对齐；{0} = 版本号
        private const string ZipNameFormat = GiteeRepo + "-v{0}.zip";

        // 与 workflow "Create split zip volumes" 步骤对齐；{0} = 版本号, {1} = 分卷编号（001..N）
        private const string SplitZipNameFormat = GiteeRepo + "-v{0}-split.zip.{1:D3}";

        // 分卷 zip 探测上限：完整安装包 ~500MB / 每卷 99MB → 5-6 卷，上限 50 兜底
        private const int MaxSplitProbe = 50;

        // 匹配 Location 头里的 4 段版本号 tag（例：/releases/tag/v1.0.0.2）
        private static readonly Regex TagPattern =
            new(@"/releases/tag/v(\d+\.\d+\.\d+\.\d+)", RegexOptions.Compiled);

        private readonly IUIDispatcher _uiDispatcher;
        private readonly IReleaseNotesFetcher _releaseNotesFetcher;
        private readonly HttpClient _redirectHttpClient;
        private readonly HttpClient _headHttpClient;
        private bool _autoPopupEnabled;

        // 后台轮询间隔：每 1 分钟检查一次是否有新版本。
        private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(1);
        private Timer? _pollingTimer;
        // 启动幂等守卫：0 = 未启动，1 = 已启动。CompareExchange 保证 StartBackgroundPolling 只建一个 Timer。
        private int _pollingStarted;
        // 单轮防重入守卫：0 = 空闲，1 = 本轮检查进行中。避免上一轮网络慢时定时器再次触发导致并发检查。
        private int _pollingTickBusy;

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public string? DownloadUrl { get; private set; }

        public string? ZipDownloadUrl { get; private set; }

        public string? SplitZipDownloadUrl { get; private set; }

        public List<string>? SplitDownloadUrls { get; private set; }

        public string? LatestReleaseNotes { get; private set; }

        public string ReleasePageUrl => GiteeReleaseLatestUrl;

        public event EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService(
            IUIDispatcher uiDispatcher,
            IAppAssemblyMarker appAssemblyMarker,
            IReleaseNotesFetcher releaseNotesFetcher)
        {
            _uiDispatcher = uiDispatcher;
            _releaseNotesFetcher = releaseNotesFetcher;
            CurrentVersion = GetCurrentVersion(appAssemblyMarker);

            // 独立句柄：一个禁 auto-redirect 用来抓 302 Location 拿最新 tag，
            // 一个允许 auto-redirect 用来 HEAD 探测 CDN 资产存在性（asset 直链会经过 foruda.gitee.com 二级跳）
            var redirectHandler = new HttpClientHandler { AllowAutoRedirect = false };
            _redirectHttpClient = new HttpClient(redirectHandler) { Timeout = TimeSpan.FromSeconds(15) };
            _redirectHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");

            _headHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _headHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");

            Debug.WriteLine($"[{nameof(UpdateService)}] 构造完成，当前版本: {CurrentVersion}");
        }

        /// <summary>
        /// 检查远端是否有新版本并刷新 <see cref="IsUpdateAvailable"/> / <see cref="LatestVersion"/> /
        /// 各类下载 URL / <see cref="LatestReleaseNotes"/>。
        ///
        /// 可用性语义：仅当"确认无新版"（latest tag 解析成功且 ≤ 当前版本）时才清空可用状态；
        /// "无法确认"（latest tag 解析失败 / 网络异常）时保留原状态不清空。这样用户主动点击
        /// "发现新版本"重查时，瞬时网络失败不会把指示按钮藏掉，下次仍可重试。
        /// </summary>
        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            Debug.WriteLine($"[{nameof(UpdateService)}.{nameof(CheckForUpdatesAsync)}] 开始检查");

            try
            {
                var latest = await FetchLatestTagAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(latest))
                {
                    // 无法解析到 latest tag（302 缺失 / 反爬 / 网络异常）：属于"无法确认"而非"确认无新版"，
                    // 保留原可用状态不清理，避免用户主动重查时因瞬时网络失败导致"发现新版本"指示消失。
                    Debug.WriteLine($"[{nameof(UpdateService)}] 未从 302 Location 解析到 latest tag");
                    return;
                }

                bool available = TryCompare(latest, CurrentVersion) > 0;
                Debug.WriteLine($"[{nameof(UpdateService)}] latest={latest}, current={CurrentVersion}, available={available}");

                if (!available)
                {
                    await ClearAvailabilityAsync().ConfigureAwait(false);
                    return;
                }

                // DownloadUrl 语义：更新弹窗里"点击打开浏览器下载"的目标——固定指向 Downloader.exe alias，
                // 不再指向带版本号的 Setup-Split.exe。用户拿到的永远是最新版下载器。
                var installerUrl = DownloaderLatestDownloadUrl;
                var zipUrl = string.Format(
                    GiteeDownloadBase + "/v{0}/" + ZipNameFormat, latest, latest);
                var splitUrls = await ProbeSplitUrlsAsync(latest).ConfigureAwait(false);
                string? splitZipUrlFirst =
                    (splitUrls is { Count: > 0 }) ? splitUrls[0] : null;
                var releaseNotes = await FetchRangeReleaseNotesAsync(CurrentVersion, latest).ConfigureAwait(false);

                // 必须 await：调用方 (App.OnStartup) 依赖"CheckForUpdatesAsync 返回时 IsUpdateAvailable 已经是最新值"
                // 来决定是否 await updateGate。fire-and-forget 会让 caller 拿到过期的 false，进而误判"无新版"。
                await SafeInvokeAsync(() =>
                {
                    IsUpdateAvailable = true;
                    LatestVersion = latest;
                    DownloadUrl = installerUrl;
                    ZipDownloadUrl = zipUrl;
                    SplitZipDownloadUrl = splitZipUrlFirst;
                    SplitDownloadUrls = splitUrls;
                    LatestReleaseNotes = releaseNotes;
                    UpdateAvailabilityChanged?.Invoke(this, true);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // 检查异常（网络 / 超时 / 反爬）属于"无法确认"而非"确认无新版"：保留原可用状态，
                // 避免后台轮询或用户主动重查时因瞬时失败把"发现新版本"指示清掉，用户失去重试入口。
                AppLog.Error(ex, nameof(UpdateService), "检查失败（静默）");
            }
        }

        public void SetAutoPopupEnabled(bool enabled) => _autoPopupEnabled = enabled;

        public void StartBackgroundPolling()
        {
            // 幂等：多次调用只建一个 Timer。
            if (System.Threading.Interlocked.CompareExchange(ref _pollingStarted, 1, 0) != 0)
                return;

            // 启动时已检测过一轮，故 dueTime 用整段间隔，避免立即重复检查。
            _pollingTimer = new Timer(OnPollingTick, null, PollingInterval, PollingInterval);
        }

        private void OnPollingTick(object? state)
        {
            // 已发现新版：现有 UI 逻辑已接管提示，无需继续轮询，停表退出。
            if (IsUpdateAvailable)
            {
                StopPolling();
                return;
            }

            // 单轮防重入：上一轮还没跑完（网络慢）时本次直接跳过。
            if (System.Threading.Interlocked.CompareExchange(ref _pollingTickBusy, 1, 0) != 0)
                return;

            _ = PollOnceAsync();
        }

        private async Task PollOnceAsync()
        {
            try
            {
                await CheckForUpdatesAsync(showNoUpdateMessage: false).ConfigureAwait(false);
                if (IsUpdateAvailable)
                    StopPolling();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                AppLog.Error(ex, nameof(UpdateService), "后台轮询检查失败（静默）");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _pollingTickBusy, 0);
            }
        }

        private void StopPolling()
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }

        private async Task ClearAvailabilityAsync()
        {
            await SafeInvokeAsync(() =>
            {
                IsUpdateAvailable = false;
                LatestVersion = null;
                DownloadUrl = null;
                ZipDownloadUrl = null;
                SplitZipDownloadUrl = null;
                SplitDownloadUrls = null;
                LatestReleaseNotes = null;
                UpdateAvailabilityChanged?.Invoke(this, false);
            }).ConfigureAwait(false);
        }

        private Task SafeInvokeAsync(Action action)
        {
            if (_uiDispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return _uiDispatcher.InvokeAsync(action);
        }

        /// <summary>
        /// 抓 `releases/latest` 的 302 Location 头拿最新 tag。
        /// Gitee 稳定行为：`releases/latest` → 302 → `releases/tag/v{version}`。
        /// 不走 auto-redirect：只读 Location 头，避免继续跳转把无关内容拖回来。
        ///
        /// 防缓存：后台每 30s 轮询都必须拿到 Gitee 当前真实 tag，不能命中 HttpClient / CDN / 代理的
        /// 任何缓存副本。故逐请求带 no-cache/no-store 头 + 唯一时间戳查询参数（cache-buster），
        /// 保证每次都是全新回源请求，避免"发布了新版但轮询仍读到旧 302 Location"。
        /// </summary>
        private async Task<string?> FetchLatestTagAsync()
        {
            var url = $"{GiteeReleaseLatestUrl}?_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
            };
            req.Headers.Pragma.ParseAdd("no-cache");

            using var resp = await _redirectHttpClient.SendAsync(req).ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.Redirect && resp.StatusCode != HttpStatusCode.Found &&
                resp.StatusCode != HttpStatusCode.MovedPermanently && resp.StatusCode != HttpStatusCode.SeeOther)
            {
                Debug.WriteLine($"[{nameof(UpdateService)}] releases/latest 未 302，实际 {(int)resp.StatusCode}");
                return null;
            }

            var location = resp.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location)) return null;

            var m = TagPattern.Match(location);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// HEAD 探测 001/002/... 直到 404 停。走 CDN foruda.gitee.com，不受 API rate limit 约束。
        /// </summary>
        private async Task<List<string>?> ProbeSplitUrlsAsync(string version)
        {
            var urls = new List<string>();
            for (int i = 1; i <= MaxSplitProbe; i++)
            {
                var name = string.Format(SplitZipNameFormat, version, i);
                var url = $"{GiteeDownloadBase}/v{version}/{name}";
                if (!await AssetExistsAsync(url).ConfigureAwait(false))
                    break;
                urls.Add(url);
            }
            return urls.Count > 0 ? urls : null;
        }

        private async Task<bool> AssetExistsAsync(string url)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var resp = await _headHttpClient.SendAsync(req).ConfigureAwait(false);
                return resp.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                AppLog.Error(ex, nameof(UpdateService), $"HEAD {url} 异常");
                return false;
            }
        }

        /// <summary>
        /// 通过 App 程序集标记接口反推 App 程序集，读取其 AssemblyInformationalVersion。
        /// </summary>
        private static string GetCurrentVersion(IAppAssemblyMarker appAssemblyMarker)
        {
            var appAssembly = appAssemblyMarker.GetType().Assembly;
            var attr = appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                var version = attr.InformationalVersion;
                var plusIndex = version.IndexOf('+');
                return plusIndex > 0 ? version[..plusIndex] : version;
            }
            return "0.0.0";
        }

        private static int TryCompare(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripVPrefix(string version) =>
            version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;

        /// <summary>
        /// 拉取 <paramref name="currentVersion"/> 到 <paramref name="latestVersion"/> 之间
        /// 所有 tag 的 release notes 并合并。若中间某个 tag 不存在（FetchAsync 返 null），
        /// 跳过该 tag。仅在前三段版本号一致时尝试枚举中间 tag；否则退回单 latest tag。
        /// </summary>
        internal async Task<string?> FetchRangeReleaseNotesAsync(string currentVersion, string latestVersion)
        {
            var tags = EnumerateIntermediateTags(currentVersion, latestVersion);
            var notesList = new List<string>();
            foreach (var tag in tags)
            {
                var notes = await _releaseNotesFetcher.FetchAsync(tag).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(notes))
                    notesList.Add(notes);
            }
            return notesList.Count > 0 ? string.Join("\n\n", notesList) : null;
        }

        /// <summary>
        /// 枚举 <paramref name="currentVersion"/> 到 <paramref name="latestVersion"/> 之间
        /// 所有可能存在的 tag（不含 current，含 latest）。
        /// 仅处理前三段相同、最后一段递增的简单场景；跨度超过 100 则退回单 latest。
        /// </summary>
        internal static List<string> EnumerateIntermediateTags(string currentVersion, string latestVersion)
        {
            var result = new List<string> { latestVersion };

            if (!Version.TryParse(StripVPrefix(currentVersion), out var current) ||
                !Version.TryParse(StripVPrefix(latestVersion), out var latest))
                return result;

            if (latest <= current) return result;

            // 仅在前面三段一致时才能按最后一段递增枚举
            if (current.Major != latest.Major ||
                current.Minor != latest.Minor ||
                current.Build != latest.Build)
                return result;

            var span = latest.Revision - current.Revision;
            if (span <= 1 || span > 100) return result;

            result.Clear();
            for (int d = current.Revision + 1; d <= latest.Revision; d++)
            {
                result.Add(new Version(current.Major, current.Minor, current.Build, d).ToString());
            }
            return result;
        }
    }
}
