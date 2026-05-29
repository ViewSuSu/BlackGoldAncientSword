using AutoUpdaterDotNET;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Events;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;

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

            // GitHub tags use 3-segment semver (e.g. v1.0.4).
            // Nerdbank.GitVersioning produces 4-segment versions (e.g. 1.0.2.35).
            // Take only the first 3 segments so AutoUpdater can compare correctly.
            var parts = CurrentVersion.Split('.');
            var semver = parts.Length >= 3
                ? string.Join(".", parts.Take(3))
                : CurrentVersion;
            AutoUpdater.InstalledVersion = new Version(semver);

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

            // Let AutoUpdater.NET handle everything. Run on background thread
            // because AutoUpdater.Start() makes synchronous HTTP calls.
            await Task.Run(() => AutoUpdater.Start(updateUrl));
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

            return "https://github.com/ViewSuSu/BlackGoldAncientSword";
        }

        /// <summary>
        /// AutoUpdater fires this event when it detects a GitHub URL.
        /// We use GitHub's releases/latest redirect (302) to find the latest tag 閳?        /// no API call, no rate limit.
        /// </summary>
        private void OnParseUpdateInfoEvent(ParseUpdateInfoEventArgs args)
        {
            var updateUrl = _customUpdateUrl ?? GetDefaultUpdateUrl();
            var match = GitHubUrlRegex.Match(updateUrl);
            if (!match.Success)
            {
                // Not a recognised GitHub repo URL; let AutoUpdater's default XML parser handle it.
                return;
            }

            var owner = match.Groups["owner"].Value;
            var repo = match.Groups["repo"].Value;

            try
            {
                // Use GitHub releases/latest redirect (no API rate limit).
                // Disable auto-redirect so we can read the Location header.
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var noRedirectClient = new HttpClient(handler) { Timeout = _httpClient.Timeout };
                noRedirectClient.DefaultRequestHeaders.Add("User-Agent", "BlackGoldAncientSword-UpdateChecker");

                var latestUrl = $"https://github.com/{owner}/{repo}/releases/latest";
                using var response = noRedirectClient.GetAsync(latestUrl).GetAwaiter().GetResult();

                // 404 means no releases exist yet
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return;
                }

                response.EnsureSuccessStatusCode();

                // Read the redirect Location header (e.g., /ViewSuSu/BlackGoldAncientSword/releases/tag/v1.0.4)
                var redirectUrl = response.Headers.Location?.ToString();
                if (string.IsNullOrWhiteSpace(redirectUrl))
                {
                    return;
                }

                // Resolve relative URL
                if (!redirectUrl.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
                {
                    redirectUrl = $"https://github.com{redirectUrl}";
                }

                // Extract tag from URL: .../releases/tag/v1.0.4
                var tagMatch = Regex.Match(redirectUrl, @"releases/tag/(?<tag>[^/]+)$", RegexOptions.IgnoreCase);
                if (!tagMatch.Success)
                {
                    return;
                }

                var tagName = tagMatch.Groups["tag"].Value;
                var version = tagName.StartsWith("v", System.StringComparison.OrdinalIgnoreCase)
                    ? tagName[1..]
                    : tagName;

                // Build download URL for the zip file (follows workflow naming convention).
                var downloadUrl = $"https://github.com/{owner}/{repo}/releases/download/{tagName}/BlackGoldAncientSword-{tagName}.zip";
                var changelogUrl = $"https://github.com/{owner}/{repo}/releases/tag/{tagName}";

                args.UpdateInfo = new UpdateInfoEventArgs
                {
                    CurrentVersion = version,
                    DownloadURL = downloadUrl,
                    ChangelogURL = changelogUrl,
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to fetch release info from GitHub: {ex.Message}", ex);
            }
        }

        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
    }
}