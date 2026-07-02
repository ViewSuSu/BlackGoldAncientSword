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
        /// 更新弹窗中"点击打开浏览器下载"目标 URL。
        /// 指向 Downloader 单文件 exe 的 `releases/latest/download/` 无版本号 alias，
        /// 由 Gitee magic redirect 永久跳到最新 release 附件，与本地版本号无关。
        /// 仅在 IsUpdateAvailable=true 时为非 null。
        /// </summary>
        string? DownloadUrl { get; }

        /// <summary>
        /// 当前最新版本的 zip 压缩包直链（Gitee Release asset 中的 .zip browser_download_url）。
        /// 给在线更新程序 BlackGoldAncientSword.Update 使用。
        /// 仅在 IsUpdateAvailable=true 且 release 含 zip asset 时为非 null。
        /// </summary>
        string? ZipDownloadUrl { get; }

        System.Collections.Generic.List<string>? SplitDownloadUrls { get; }

        /// <summary>
        /// 最新版本的 release notes（Gitee Release "body"，markdown 源码）。
        /// 仅在 IsUpdateAvailable=true 时有值；可能为空字符串。
        /// </summary>
        string? LatestReleaseNotes { get; }

        /// <summary>
        /// Gitee Releases 最新版页面 URL，含 release notes 与所有资产列表。
        /// 始终可用，无需等待 API 检查完成。
        /// </summary>
        string ReleasePageUrl { get; }

        event System.EventHandler<bool>? UpdateAvailabilityChanged;
    }
}
