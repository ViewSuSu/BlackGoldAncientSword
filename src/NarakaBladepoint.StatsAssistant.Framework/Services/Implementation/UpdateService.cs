using AutoUpdaterDotNET;
using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
using NarakaBladepoint.StatsAssistant.Framework.Core.Events;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;
using Prism.Events;
using System.Reflection;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private readonly IEventAggregator _eventAggregator;
        private string? _customUpdateUrl;
        private bool _showNotifications = true;
        private bool _isChecking;

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public event System.EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            CurrentVersion = GetCurrentVersion();
        }

        public void Configure(string? customUpdateUrl = null)
        {
            _customUpdateUrl = customUpdateUrl;

            // AutoUpdater uses the entry assembly version by default,
            // but we can set it explicitly for safety
            AutoUpdater.InstalledVersion = new Version(CurrentVersion);

            // Wire up UI callbacks
            AutoUpdater.CheckForUpdateEvent += OnCheckForUpdateEvent;
            AutoUpdater.ApplicationExitEvent += () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
            };
        }

        public async System.Threading.Tasks.Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            if (_isChecking) return;
            _showNotifications = showNoUpdateMessage;
            _isChecking = true;
            var updateUrl = _customUpdateUrl ?? GetDefaultUpdateUrl();
            AutoUpdater.Start(updateUrl);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            _isChecking = false;

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

                var product = productAttr?.Product ?? "NarakaBladepoint-Stats-Assistant";
                // GitHub releases API URL format
                // Override this with your actual GitHub repo URL in Configure()
                return $"https://github.com/{product}";
            }

            return "https://github.com/NarakaBladepoint-Stats-Assistant";
        }

        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
    }
}
