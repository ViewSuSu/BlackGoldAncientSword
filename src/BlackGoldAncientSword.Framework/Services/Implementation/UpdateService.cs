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
        private const string GitHubLatestReleaseApi =
            "https://api.github.com/repos/ViewSuSu/BlackGoldAncientSword/releases/latest";
        private const string GitHubLatestReleasePage =
            "https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest";

        private readonly IUIDispatcher _uiDispatcher;
        private readonly HttpClient _httpClient;
        private bool _autoPopupEnabled;

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public string? DownloadUrl { get; private set; }

        public string ReleasePageUrl => GitHubLatestReleasePage;

        public event EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService(IUIDispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher;
            CurrentVersion = GetCurrentVersion();

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
                var installerUrl = ResolveInstallerUrl(root);

                bool available = TryCompare(latest, CurrentVersion) > 0;
                Debug.WriteLine($"[{nameof(UpdateService)}] tag={tagName}, current={CurrentVersion}, available={available}");

                SafeInvoke(() =>
                {
                    IsUpdateAvailable = available;
                    LatestVersion = available ? latest : null;
                    DownloadUrl = available ? installerUrl : null;
                    UpdateAvailabilityChanged?.Invoke(this, available);
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.WriteLine($"[{nameof(UpdateService)}] GitHub API 检查失败（静默）: {ex.Message}");

                // 静默失败：仍触发 false 让监听方退出 "checking" 占位状态
                SafeInvoke(() =>
                {
                    IsUpdateAvailable = false;
                    LatestVersion = null;
                    DownloadUrl = null;
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

        private static string GetCurrentVersion()
        {
            var attr = typeof(UpdateService).Assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                var version = attr.InformationalVersion;
                var plusIndex = version.IndexOf('+');
                return plusIndex > 0 ? version[..plusIndex] : version;
            }
            return "0.0.0";
        }

        /// <summary>
        /// 去掉 git tag 常见的 "v" 前缀（如 "v1.0.0.12" → "1.0.0.12"）。
        /// </summary>
        private static string NormalizeVersion(string version)
        {
            if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V'))
                return version[1..];
            return version;
        }

        private static string? ResolveInstallerUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameEl)) continue;
                var name = nameEl.GetString();
                if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (asset.TryGetProperty("browser_download_url", out var urlEl))
                        return urlEl.GetString();
                }
            }
            return null;
        }

        private static int TryCompare(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
