using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.UpdateNotification.ViewModels
{
    public class UpdateNotificationPageViewModel : ViewModelBase
    {
        private const string GitHubLatestReleaseApi =
            "https://api.github.com/repos/ViewSuSu/BlackGoldAncientSword/releases/latest";
        private const string GitHubLatestReleasePage =
            "https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest";

        private readonly IUpdateService _updateService;

        private string _latestVersion = string.Empty;
        public string LatestVersion
        {
            get => _latestVersion;
            set
            {
                if (_latestVersion == value) return;
                _latestVersion = value;
                RaisePropertyChanged(nameof(LatestVersion));
            }
        }

        private string _downloadUrl = GitHubLatestReleasePage;
        public string DownloadUrl
        {
            get => _downloadUrl;
            set
            {
                if (_downloadUrl == value) return;
                _downloadUrl = value;
                RaisePropertyChanged(nameof(DownloadUrl));
            }
        }

        public UpdateNotificationPageViewModel(IUpdateService updateService)
        {
            _updateService = updateService;
            LatestVersion = _updateService.LatestVersion ?? string.Empty;
            ResolveDownloadUrlAsync().SafeFireAndForget("UpdateNotification.ResolveDownloadUrl");
        }

        private async Task ResolveDownloadUrlAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");
                var json = await http.GetStringAsync(GitHubLatestReleaseApi).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var assets = doc.RootElement.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var url = asset.GetProperty("browser_download_url").GetString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            DownloadUrl = url;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(ResolveDownloadUrlAsync)}] GitHub API 查询失败，使用 releases 页面回退: {ex.Message}");
            }
        }

        private DelegateCommand? _openDownloadCommand;
        public DelegateCommand OpenDownloadCommand =>
            _openDownloadCommand ??= new DelegateCommand(() =>
            {
                try
                {
                    var url = string.IsNullOrEmpty(DownloadUrl) ? GitHubLatestReleasePage : DownloadUrl;
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    DismissOverlay();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OpenDownloadCommand)}] 打开下载页失败: {ex}");
                }
            });

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(DismissOverlay);

        private void DismissOverlay()
        {
            var region = regionManager.Regions[GlobalConstant.UpdateNotificationRegion];
            region.RemoveAll();
        }
    }
}
