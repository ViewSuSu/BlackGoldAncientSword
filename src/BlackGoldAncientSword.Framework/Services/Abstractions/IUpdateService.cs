namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IUpdateService
    {
        System.Threading.Tasks.Task CheckForUpdatesAsync(bool showNoUpdateMessage = true);

        string CurrentVersion { get; }


        bool IsUpdateAvailable { get; }

        string? LatestVersion { get; }

        event System.EventHandler<bool>? UpdateAvailabilityChanged;
    }
}