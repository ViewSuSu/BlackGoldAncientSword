namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IUpdateService
    {
        System.Threading.Tasks.Task CheckForUpdatesAsync(bool showNoUpdateMessage = true);

        void SetAutoPopupEnabled(bool enabled);

        string CurrentVersion { get; }

        bool IsUpdateAvailable { get; }

        string? LatestVersion { get; }

        event System.EventHandler<bool>? UpdateAvailabilityChanged;
    }
}
