using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private const string GitHubOwner = "ViewSuSu";
        private const string GitHubRepo = "BlackGoldAncientSword";
        private const string GitHubLatestReleaseApi =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";
        private const string GitHubLatestReleasePage =
            "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        private const string GiteeOwner = "SususuChang";
        private const string GiteeRepo = "BlackGoldAncientSword";
        private const string GiteeReleasePage =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases/latest";
        private const string GiteeDownloadBase =
            "https://gitee.com/" + GiteeOwner + "/" + GiteeRepo + "/releases/download";
        private const string GiteeLatestReleaseApi =
            "https://gitee.com/api/v5/repos/" + GiteeOwner + "/" + GiteeRepo + "/releases/latest";
        private const string SplitZipFormat = GiteeRepo + "-v{0}-split.zip.001";

        /// <summary>
        /// 安装包命名约定（setup.iss 生成）：BlackGoldAncientSword-{version}-win-x64-Setup.exe
        /// ResolveAssetUrl 按 .exe 后缀匹配，无需关心完整文件名。
        /// release zip 命名约定：BlackGoldAncientSword-v{version}.zip
        /// CI workflow (.github/workflows/dotnet-desktop.yml) 生成此名。
        /// </summary>
        private const string ZipNameFormat = GitHubRepo + "-v{0}.zip";

        private readonly IUIDispatcher _uiDispatcher;
        private readonly HttpClient _httpClient;
        private bool _autoPopupEnabled;

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public string? DownloadUrl { get; private set; }

        public string? ZipDownloadUrl { get; private set; }

        public string? SplitZipDownloadUrl { get; private set; }

        public List<string>? SplitDownloadUrls { get; private set; }

        public string? LatestReleaseNotes { get; private set; }

        public string ReleasePageUrl => GiteeReleasePage;

        public event EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService(IUIDispatcher uiDispatcher, IAppAssemblyMarker appAssemblyMarker)
        {
            _uiDispatcher = uiDispatcher;
            CurrentVersion = GetCurrentVersion(appAssemblyMarker);

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            Debug.WriteLine($"[{nameof(UpdateService)}] 构造完成，当前版本: {CurrentVersion}");
        }

        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            Debug.WriteLine($"[{nameof(UpdateService)}.{nameof(CheckForUpdatesAsync)}] 开始检查");

            try
            {
                var json = await _httpClient.GetStringAsync(GitHubLatestReleaseApi).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var tagEl)
                    ? (tagEl.GetString() ?? string.Empty)
                    : string.Empty;
                var latest = NormalizeVersion(tagName);
                var installerUrl = ResolveAssetUrl(root, ".exe");
                var zipUrl = ResolveZipUrl(root, latest);
                var splitUrl = ResolveSplitZipUrl(root);
                var notes = root.TryGetProperty("body", out var bodyEl)
                    ? (bodyEl.GetString() ?? string.Empty)
                    : string.Empty;

                bool available = TryCompare(latest, CurrentVersion) > 0;

                // 从 Gitee API 获取所有分卷 zip 的下载URL和精确大小
                List<string>? splitUrls = null;
                if (available)
                {
                    splitUrls = await FetchSplitAssetsFromGiteeAsync().ConfigureAwait(false);
                }
                Debug.WriteLine($"[{nameof(UpdateService)}] tag={tagName}, current={CurrentVersion}, available={available}");

                SafeInvoke(() =>
                {
                    IsUpdateAvailable = available;
                    LatestVersion = available ? latest : null;
                    DownloadUrl = available ? installerUrl : null;
                    ZipDownloadUrl = available ? zipUrl : null;
                    SplitZipDownloadUrl = available && splitUrl != null
                        ? string.Format(GiteeDownloadBase + "/v{0}/" + SplitZipFormat, latest, latest)
                        : null;
                    SplitDownloadUrls = available ? splitUrls : null;
                    LatestReleaseNotes = available ? notes : null;
                    UpdateAvailabilityChanged?.Invoke(this, available);
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.WriteLine($"[{nameof(UpdateService)}] GitHub API 检查失败（静默）: {ex.Message}");

                SafeInvoke(() =>
                {
                    IsUpdateAvailable = false;
                    LatestVersion = null;
                    DownloadUrl = null;
                    ZipDownloadUrl = null;
                    SplitZipDownloadUrl = null;
                    SplitDownloadUrls = null;
                    LatestReleaseNotes = null;
                    UpdateAvailabilityChanged?.Invoke(this, false);
                });
            }
        }

        public void SetAutoPopupEnabled(bool enabled) => _autoPopupEnabled = enabled;

        private void SafeInvoke(Action action)
        {
            if (_uiDispatcher.CheckAccess())
                action();
            else
                _uiDispatcher.BeginInvoke(action);
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

        /// <summary>去掉 git tag 常见的 "v" 前缀。</summary>
        private static string NormalizeVersion(string version)
        {
            if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V'))
                return version[1..];
            return version;
        }

        private static string? ResolveAssetUrl(JsonElement root, string extension)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameEl)) continue;
                var name = nameEl.GetString();
                if (name != null && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    if (asset.TryGetProperty("browser_download_url", out var urlEl))
                        return urlEl.GetString();
                }
            }
            return null;
        }

        /// <summary>
        /// 找版本对应的 zip 资产：优先精确匹配 BlackGoldAncientSword-v{version}.zip，
        /// 找不到再退到任意 .zip。version 已去掉 "v" 前缀。
        /// </summary>
        private static string? ResolveZipUrl(JsonElement root, string version)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            var expected = string.Format(ZipNameFormat, version);
            string? fallback = null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameEl)) continue;
                var name = nameEl.GetString();
                if (name == null) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (!asset.TryGetProperty("browser_download_url", out var urlEl)) continue;

                var url = urlEl.GetString();
                if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[{nameof(UpdateService)}] 命中版本 zip: {name}");
                    return url;
                }
                fallback ??= url;
            }

            if (fallback != null)
                Debug.WriteLine($"[{nameof(UpdateService)}] 未找到 {expected}，退回首个 .zip");
            return fallback;
        }


        /// <summary>
        /// 找第一个 .zip.001 分卷资产的下载 URL。
        /// 新版 Updater 拿到此 URL 后自动枚举 .001/.002/... 下载全部分卷、合并后解压。
        /// 旧版 Updater 仍需保持单 .zip 兼容（ZipDownloadUrl），两者互不干扰。
        /// </summary>
        private static string? ResolveSplitZipUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameEl)) continue;
                var name = nameEl.GetString();
                if (name == null) continue;
                if (!name.EndsWith(".zip.001", StringComparison.OrdinalIgnoreCase)) continue;
                if (!asset.TryGetProperty("browser_download_url", out var urlEl)) continue;

                return urlEl.GetString();
            }
            return null;
        }

        /// <summary>
        /// 从 Gitee release API 获取所有 .zip.XXX 分卷的下载 URL 列表和精确总大小。
        /// 用独立 HttpClient 避免 _httpClient 的 GitHub 专用 Accept 头污染。
        /// </summary>
        /// <summary>
        /// 从 Gitee release API 获取所有 .zip.XXX 分卷的下载 URL 列表。
        /// Gitee API 不返回 asset size，总大小由 Updater 通过 HEAD 请求获取。
        /// 用独立 HttpClient 避免 _httpClient 的 GitHub 专用 Accept 头污染。
        /// </summary>
        private async Task<List<string>?> FetchSplitAssetsFromGiteeAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                var json = await http.GetStringAsync(GiteeLatestReleaseApi).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                    return null;

                var urls = new List<string>();
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameEl)) continue;
                    var name = nameEl.GetString();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!name.Contains(".zip.")) continue;
                    if (!asset.TryGetProperty("browser_download_url", out var urlEl)) continue;
                    urls.Add(urlEl.GetString()!);
                }
                return urls.Count > 0 ? urls : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(UpdateService)}] Gitee API 获取分卷列表失败: {ex.Message}");
                return null;
            }
        }


        private static int TryCompare(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
