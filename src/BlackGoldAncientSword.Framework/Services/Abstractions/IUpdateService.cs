namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IUpdateService
    {
        /// <summary>
        /// Manually check for updates and prompt user if available.
        /// </summary>
        System.Threading.Tasks.Task CheckForUpdatesAsync(bool showNoUpdateMessage = true);

        /// <summary>
        /// Current application version string (from Nerdbank.GitVersioning).
        /// </summary>
        string CurrentVersion { get; }

        /// <summary>
        /// Configure the update service (called once at startup).
        /// </summary>
        void Configure(string? customUpdateUrl = null);

        /// <summary>
        /// Whether an update is currently available.
        /// </summary>
        bool IsUpdateAvailable { get; }

        /// <summary>
        /// The latest available version string, or null if no update is available.
        /// </summary>
        string? LatestVersion { get; }

        /// <summary>
        /// Raised when update availability changes.
        /// </summary>
        event System.EventHandler<bool>? UpdateAvailabilityChanged;
    }
}
