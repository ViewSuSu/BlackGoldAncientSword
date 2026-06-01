using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.IO;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// Update manifest in cc-switch/tauri-plugin-updater "latest.json" format.
    /// </summary>
    internal sealed class LatestJsonManifest
    {
        [JsonProperty("version")]
        public string? Version { get; set; }

        [JsonProperty("notes")]
        public string? Notes { get; set; }

        [JsonProperty("pub_date")]
        public string? PubDate { get; set; }

        [JsonProperty("platforms")]
        public Dictionary<string, PlatformInfo>? Platforms { get; set; }
    }

    internal sealed class PlatformInfo
    {
        [JsonProperty("signature")]
        public string? Signature { get; set; }

        [JsonProperty("url")]
        public string? Url { get; set; }
    }

    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private bool _autoPopupEnabled;
        private CancellationTokenSource? _downloadCts;

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public string? DownloadUrl { get; private set; }

        public string? ReleaseNotes { get; private set; }

        public event EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService()
        {
            Debug.WriteLine("[UpdateService] 使用 cc-switch 兼容 latest.json 更新机制");

            CurrentVersion = GetCurrentVersion();
            Debug.WriteLine($"[UpdateService] 当前版本: {CurrentVersion}");

            // Set User-Agent for GitHub API compatibility
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword-Updater/1.0");
        }

        /// <summary>
        /// Checks GitHub Releases for latest.json and reports whether an update is available.
        /// Mirrors cc-switch''s tauri-plugin-updater behavior.
        /// </summary>
        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdatesAsync, showNoUpdateMessage={showNoUpdateMessage}");

            try
            {
                var url = GetLatestJsonUrl();
                Debug.WriteLine($"[UpdateService] 获取 latest.json: {url}");

                var json = await _httpClient.GetStringAsync(url);
                var manifest = JsonConvert.DeserializeObject<LatestJsonManifest>(json);

                if (manifest == null || string.IsNullOrEmpty(manifest.Version))
                {
                    Debug.WriteLine("[UpdateService] latest.json 无效或版本为空");
                    if (showNoUpdateMessage)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show(
                                Application.Current.TryFindResource("UpdateDialog.CannotDownloadAppcast") as string
                                ?? "Unable to connect to update server.",
                                Application.Current.TryFindResource("UpdateDialog.ErrorTitle") as string ?? "Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return;
                }

                var latestVersion = manifest.Version;
                Debug.WriteLine($"[UpdateService] 最新版本: {latestVersion}");

                // Get platform-specific download URL (windows-x86_64, matching cc-switch)
                var platformKey = "windows-x86_64";
                if (manifest.Platforms?.TryGetValue(platformKey, out var platformInfo) == true
                    && !string.IsNullOrEmpty(platformInfo.Url))
                {
                    DownloadUrl = platformInfo.Url;
                    ReleaseNotes = manifest.Notes;
                }
                else
                {
                    Debug.WriteLine($"[UpdateService] 未找到 {platformKey} 平台下载信息");
                    if (showNoUpdateMessage)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show(
                                Application.Current.TryFindResource("UpdateDialog.UpToDate") as string
                                ?? "Your current version is up to date.",
                                Application.Current.TryFindResource("UpdateDialog.InfoTitle") as string ?? "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information));
                    }
                    return;
                }

                // Compare versions (cc-switch style: simple semver comparison)
                if (IsNewerVersion(latestVersion, CurrentVersion))
                {
                    Debug.WriteLine($"[UpdateService] 发现新版本: {latestVersion} > {CurrentVersion}");
                    IsUpdateAvailable = true;
                    LatestVersion = latestVersion;

                    Application.Current.Dispatcher.Invoke(() =>
                        UpdateAvailabilityChanged?.Invoke(this, true));

                    // Show update dialog if auto-popup is enabled or user manually checked
                    if (showNoUpdateMessage || _autoPopupEnabled)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            ShowUpdateAvailableDialog(latestVersion, manifest.Notes));
                    }
                }
                else
                {
                    Debug.WriteLine($"[UpdateService] 已是最新版本: {CurrentVersion} >= {latestVersion}");
                    IsUpdateAvailable = false;
                    LatestVersion = null;

                    Application.Current.Dispatcher.Invoke(() =>
                        UpdateAvailabilityChanged?.Invoke(this, false));

                    if (showNoUpdateMessage)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show(
                                Application.Current.TryFindResource("UpdateDialog.UpToDate") as string
                                ?? "Your current version is up to date.",
                                Application.Current.TryFindResource("UpdateDialog.InfoTitle") as string ?? "Info",
                                MessageBoxButton.OK, MessageBoxImage.Information));
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[UpdateService] 网络错误: {ex.Message}");
                if (showNoUpdateMessage)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show(
                            Application.Current.TryFindResource("UpdateDialog.CannotDownloadAppcast") as string
                            ?? "Unable to connect to update server.",
                            Application.Current.TryFindResource("UpdateDialog.ErrorTitle") as string ?? "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 检查更新失败: {ex.Message}");
                if (showNoUpdateMessage)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show(
                            string.Format(
                                Application.Current.TryFindResource("UpdateDialog.DownloadError") as string
                                ?? "Update check failed: {0}", ex.Message),
                            Application.Current.TryFindResource("UpdateDialog.ErrorTitle") as string ?? "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }
        }

        /// <summary>
        /// Downloads and installs the update (cc-switch behavior: download, then run installer).
        /// </summary>
        public async Task DownloadAndInstallAsync(IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                Debug.WriteLine("[UpdateService] DownloadUrl 为空，无法下载");
                return;
            }

            Debug.WriteLine($"[UpdateService] 开始下载: {DownloadUrl}");

            try
            {
                _downloadCts = new CancellationTokenSource();
                var response = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var tempFile = Path.Combine(Path.GetTempPath(), $"BlackGoldAncientSword_Setup_v{LatestVersion}.msi");

                await using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
                await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, _downloadCts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _downloadCts.Token);
                    totalRead += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report((int)(totalRead * 100 / totalBytes));
                    }
                }

                Debug.WriteLine($"[UpdateService] 下载完成: {tempFile}");

                // Run the installer (mirrors cc-switch''s tauri updater behavior)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "msiexec.exe",
                                Arguments = $"/i \"{tempFile}\" /passive",
                                UseShellExecute = true
                            }
                        };
                        process.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UpdateService] 启动安装程序失败: {ex.Message}");
                        // Fallback: open the file directly
                        Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                    }

                    // Close the app so the installer can replace files (cc-switch behavior)
                    Application.Current.Shutdown();
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[UpdateService] 下载已取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 下载失败: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show(
                        string.Format(
                            Application.Current.TryFindResource("UpdateDialog.DownloadError") as string
                            ?? "Download failed: {0}", ex.Message),
                        Application.Current.TryFindResource("UpdateDialog.ErrorTitle") as string ?? "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        /// <summary>
        /// Cancels an in-progress download.
        /// </summary>
        public void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        public void SetAutoPopupEnabled(bool enabled)
        {
            _autoPopupEnabled = enabled;
        }

        /// <summary>
        /// Shows an update-available dialog and offers to download/install.
        /// Mirrors cc-switch''s AboutSection update button behavior.
        /// </summary>
        private void ShowUpdateAvailableDialog(string latestVersion, string? notes)
        {
            var message = string.Format(
                Application.Current.TryFindResource("UpdateDialog.VersionInfo") as string
                ?? "{0} is now available (you have {1}). Would you like to update now?",
                latestVersion, CurrentVersion);

            var title = Application.Current.TryFindResource("UpdateDialog.SoftwareUpdate") as string
                        ?? "Software Update";

            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                Task.Run(async () => await DownloadAndInstallAsync());
            }
        }

        /// <summary>
        /// Semantic version comparison matching cc-switch''s behavior.
        /// Strips leading 'v' if present.
        /// </summary>
        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var v1 = new Version(latest.TrimStart('v'));
                var v2 = new Version(current.TrimStart('v'));
                return v1 > v2;
            }
            catch
            {
                // Fallback to string comparison
                return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        /// <summary>
        /// Reads the current version from AssemblyInformationalVersionAttribute,
        /// stripping Git commit hash suffix (mirrors cc-switch''s simple semver).
        /// </summary>
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
        /// cc-switch compatible update endpoint.
        /// Uses GitHub Releases latest.json pattern:
        /// https://github.com/{owner}/{repo}/releases/latest/download/latest.json
        /// </summary>
        private static string GetLatestJsonUrl()
        {
            // Same pattern as cc-switch: releases/latest/download/latest.json
            const string repoOwner = "ViewSuSu";
            const string repoName = "BlackGoldAncientSword";
            return $"https://github.com/{repoOwner}/{repoName}/releases/latest/download/latest.json";
        }
    }
}
