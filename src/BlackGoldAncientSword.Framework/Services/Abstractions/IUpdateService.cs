namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IUpdateService
    {
        System.Threading.Tasks.Task CheckForUpdatesAsync(bool showNoUpdateMessage = true);

        string CurrentVersion { get; }

        void Configure(string? customAppcastUrl = null);

        bool IsUpdateAvailable { get; }

        string? LatestVersion { get; }

        event System.EventHandler<bool>? UpdateAvailabilityChanged;
    }
}