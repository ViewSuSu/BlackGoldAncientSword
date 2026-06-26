namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IUpdateService
    {
        System.Threading.Tasks.Task CheckForUpdatesAsync(bool showNoUpdateMessage = true);

        void SetAutoPopupEnabled(bool enabled);

        string CurrentVersion { get; }

        bool IsUpdateAvailable { get; }

        string? LatestVersion { get; }

        /// <summary>
        /// 当前最新版本的安装包直链（GitHub Release asset 中的 .exe browser_download_url）。
        /// 仅在 IsUpdateAvailable=true 时为非 null。
        /// </summary>
        string? DownloadUrl { get; }

        /// <summary>
        /// GitHub Releases 最新版页面 URL，含 release notes 与所有资产列表。
        /// 始终可用，无需等待 API 检查完成。
        /// </summary>
        string ReleasePageUrl { get; }

        event System.EventHandler<bool>? UpdateAvailabilityChanged;
    }
}
