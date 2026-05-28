using AutoUpdaterDotNET;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Events;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private readonly IEventAggregator _eventAggregator;
        private string? _customUpdateUrl;
        private bool _showNotifications = true;
        private bool _isChecking;
        private static readonly Regex GitHubUrlRegex = new(
            @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HttpClient _httpClient = new()
        {
            DefaultRequestHeaders = { { "User-Agent", "BlackGoldAncientSword-UpdateChecker" } }
        };

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public event System.EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            CurrentVersion = GetCurrentVersion();
            _httpClient.Timeout = System.TimeSpan.FromSeconds(15);
        }

        public void Configure(string? customUpdateUrl = null)
        {
            _customUpdateUrl = customUpdateUrl;

            // AutoUpdater uses the entry assembly version by default,
            // but we can set it explicitly for safety
            AutoUpdater.InstalledVersion = new Version(CurrentVersion);

            // Wire up UI callbacks
            AutoUpdater.CheckForUpdateEvent += OnCheckForUpdateEvent;
            AutoUpdater.ParseUpdateInfoEvent += OnParseUpdateInfoEvent;
            AutoUpdater.ApplicationExitEvent += () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
            };
        }

        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            if (_isChecking) return;
            _showNotifications = showNoUpdateMessage;
            _isChecking = true;

            var updateUrl = _customUpdateUrl ?? GetDefaultUpdateUrl();
            var match = GitHubUrlRegex.Match(updateUrl);
            if (!match.Success)
            {
                // Non-GitHub URL; let AutoUpdater handle it normally
                AutoUpdater.Start(updateUrl);
                return;
            }

            // GitHub URL: bypass AutoUpdater's built-in GitHub handling
            // to avoid 404 errors when the repo is private, missing, or unreachable.
            try
            {
                var owner = match.Groups["owner"].Value;
                var repo = match.Groups["repo"].Value;
                var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=1";

                using var response = await _httpClient.GetAsync(apiUrl);

                if (response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    ReportNoUpdate();
                    return;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var releases = doc.RootElement;

                if (releases.GetArrayLength() == 0)
                {
                    ReportNoUpdate();
                    return;
                }

                var latest = releases[0];
                var tagName = latest.GetProperty("tag_name").GetString() ?? "";
                var htmlUrl = latest.GetProperty("html_url").GetString() ?? "";

                var latestVersion = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tagName[1..]
                    : tagName;

                var normalizedLatest = NormalizeVersionSegments(latestVersion, InstalledVersionSegments);
                var currentVer = new Version(CurrentVersion);
                var latestVer = new Version(normalizedLatest);

                // Standard numeric comparison first
                bool isNewer = latestVer > currentVer;

                // Handle Nerdbank.GitVersioning height-based versions:
                // e.g., installed "1.0.28" (version 1.0 + 28 commits height)
                // vs GitHub tag "1.0.3" (semver patch release).
                // When major.minor matches and the GitHub tag exists,
                // treat it as an update even if the 3rd segment is numerically lower.
                if (!isNewer
                    && latestVer.Major == currentVer.Major
                    && latestVer.Minor == currentVer.Minor
                    && latestVer != currentVer)
                {
                    isNewer = true;
                }

                IsUpdateAvailable = isNewer;
                LatestVersion = IsUpdateAvailable ? normalizedLatest : null;
                UpdateAvailabilityChanged?.Invoke(this, IsUpdateAvailable);

                if (!IsUpdateAvailable)
                {
                    ReportNoUpdate();
                    return;
                }

                ReportUpdateAvailable(htmlUrl);
            }
            catch (Exception)
            {
                // Network or parsing error; treat as no update to avoid spamming the user
                ReportNoUpdate();
            }
        }

        private void OnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            _isChecking = false;

            if (args?.Error != null)
            {
                if (_showNotifications)
                {
                    _eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(
                            string.Format(L("Settings.UpdateError", "Update check failed: {0}"), args.Error.Message),
                            new List<string> { "Error" }));
                }
                return;
            }

            if (args == null)
            {
                if (_showNotifications)
                {
                    _eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(
                            L("Settings.UpdateCheckFailed", "Failed to check for updates. Please try again later."),
                            new List<string> { "Error" }));
                }
                return;
            }

            IsUpdateAvailable = args.IsUpdateAvailable;
            LatestVersion = args.IsUpdateAvailable ? args.CurrentVersion : null;
            UpdateAvailabilityChanged?.Invoke(this, args.IsUpdateAvailable);

            if (!args.IsUpdateAvailable)
            {
                if (_showNotifications)
                {
                    _eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(
                            L("Settings.NoUpdateAvailable", "You are running the latest version.")));
                }
                return;
            }

            // Update is available; show dialog only for manual checks
            try
            {
                if (_showNotifications)
                {
                    AutoUpdater.ShowUpdateForm(args);
                }
            }
            catch (Exception ex)
            {
                if (_showNotifications)
                {
                    _eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(
                            string.Format(L("Settings.UpdateError", "Update error: {0}"), ex.Message),
                            new List<string> { "Error" }));
                }
            }
        }

        private static string GetCurrentVersion()
        {
            // Try to get version from Nerdbank.GitVersioning generated ThisAssembly
            var entryAsm = Assembly.GetEntryAssembly();
            if (entryAsm != null)
            {
                var thisAssemblyType = entryAsm.GetType("ThisAssembly");
                if (thisAssemblyType != null)
                {
                    var field = thisAssemblyType.GetField("AssemblyInformationalVersion");
                    if (field != null)
                    {
                        var value = field.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var plusIndex = value.IndexOf('+');
                            return plusIndex > 0 ? value[..plusIndex] : value;
                        }
                    }
                }

                // Fallback to AssemblyInformationalVersionAttribute
                var attr = entryAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null)
                {
                    var version = attr.InformationalVersion;
                    var plusIndex = version.IndexOf('+');
                    return plusIndex > 0 ? version[..plusIndex] : version;
                }

                // Last fallback: assembly file version
                var fileVersionAttr = entryAsm.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (fileVersionAttr != null)
                {
                    return fileVersionAttr.Version;
                }
            }

            return "1.0.0";
        }

        private static string GetDefaultUpdateUrl()
        {
            // Default: try to detect from assembly metadata
            var entryAsm = Assembly.GetEntryAssembly();
            if (entryAsm != null)
            {
                var productAttr = entryAsm.GetCustomAttribute<AssemblyProductAttribute>();
                var companyAttr = entryAsm.GetCustomAttribute<AssemblyCompanyAttribute>();

                var product = productAttr?.Product ?? "BlackGoldAncientSword";
                // GitHub releases API URL format
                // Override this with your actual GitHub repo URL in Configure()
                return $"https://github.com/{product}";
            }

            return "https://github.com/ViewSuSu/NarakaBladepoint-Stats-Assistant";
        }

        private void OnParseUpdateInfoEvent(ParseUpdateInfoEventArgs args)
        {
            // Get the update URL that was configured
            var updateUrl = _customUpdateUrl ?? GetDefaultUpdateUrl();

            var match = GitHubUrlRegex.Match(updateUrl);
            if (!match.Success)
            {
                // Not a GitHub URL; let the default XML parser handle it
                return;
            }

            var owner = match.Groups["owner"].Value;
            var repo = match.Groups["repo"].Value;
            var latestUrl = $"https://github.com/{owner}/{repo}/releases/latest";

            try
            {
                // Use GitHub releases/latest redirect (no API rate limit).
                // Disable auto-redirect so we can read the Location header.
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var noRedirectClient = new HttpClient(handler) { Timeout = _httpClient.Timeout };
                noRedirectClient.DefaultRequestHeaders.Add("User-Agent", "BlackGoldAncientSword-UpdateChecker");
                using var response = noRedirectClient.GetAsync(latestUrl).GetAwaiter().GetResult();

                // 404 means no releases exist yet
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return;
                }

                response.EnsureSuccessStatusCode();

                // Read the redirect Location header (e.g., /ViewSuSu/BlackGoldAncientSword/releases/tag/v1.0.2)
                var redirectUrl = response.Headers.Location?.ToString();
                if (string.IsNullOrWhiteSpace(redirectUrl))
                {
                    return;
                }

                // Resolve relative URL
                if (!redirectUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    redirectUrl = $"https://github.com{redirectUrl}";
                }

                // Extract tag from URL: .../releases/tag/v1.0.2
                var tagMatch = Regex.Match(redirectUrl, @"releases/tag/(?<tag>[^/]+)$", RegexOptions.IgnoreCase);
                if (!tagMatch.Success)
                {
                    return;
                }

                var tagName = tagMatch.Groups["tag"].Value;
                // Strip leading 'v' from tag name to get the version string
                var latestVersion = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tagName[1..]
                    : tagName;

                var normalizedLatest = NormalizeVersionSegments(latestVersion, InstalledVersionSegments);

                // Derive the release page URL (changelog)
                var changelogUrl = $"https://github.com/{owner}/{repo}/releases/tag/{tagName}";

                args.UpdateInfo = new UpdateInfoEventArgs
                {
                    CurrentVersion = normalizedLatest,
                    DownloadURL = changelogUrl,
                    ChangelogURL = changelogUrl,
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to fetch release info from GitHub: {ex.Message}", ex);
            }

        }

        /// <summary>
        /// Number of segments in the installed version (e.g., "1.0.2.30" has 4 segments).
        /// </summary>
        private int InstalledVersionSegments =>
            string.IsNullOrEmpty(CurrentVersion) ? 3 : CurrentVersion.Split('.').Length;

        /// <summary>
        /// Normalize a version string to have the same number of dot-separated segments
        /// as the installed version, padding with zeros as needed.
        /// This ensures System.Version comparison works properly when segment counts differ.
        /// </summary>
        private string NormalizeVersionSegments(string version, int targetSegments)
        {
            var parts = version.Split('.');
            if (parts.Length >= targetSegments)
                return version;

            // Pad with zeros to match the target segment count
            var padded = new string[targetSegments];
            for (int i = 0; i < targetSegments; i++)
            {
                padded[i] = i < parts.Length ? parts[i] : "0";
            }
            return string.Join(".", padded);
        }

        private void ReportNoUpdate()
        {
            _isChecking = false;
            IsUpdateAvailable = false;
            LatestVersion = null;
            if (_showNotifications)
            {
                _eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(
                        L("Settings.NoUpdateAvailable", "You are running the latest version.")));
            }
        }

        private void ReportUpdateAvailable(string downloadUrl)
        {
            _isChecking = false;
            if (_showNotifications)
            {
                var args = new UpdateInfoEventArgs
                {
                    CurrentVersion = LatestVersion ?? "",
                    DownloadURL = downloadUrl,
                    ChangelogURL = downloadUrl,
                };
                try
                {
                    AutoUpdater.ShowUpdateForm(args);
                }
                catch
                {
                    _eventAggregator.GetEvent<TipMessageEvent>()
                        .Publish(new TipMessageWithHighlightArgs(
                            L("Settings.NoUpdateAvailable", "You are running the latest version.")));
                }
            }
        }

        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
    }
}
