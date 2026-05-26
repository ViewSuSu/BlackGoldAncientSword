namespace NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions
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
    }
}
