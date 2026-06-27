using System;
using System.Diagnostics;
using System.IO;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.UpdateNotification.ViewModels
{
    public class UpdateNotificationPageViewModel : ViewModelBase
    {
        private const string UpdaterExeName = "BlackGoldAncientSword.Update.exe";
        private const string MainAppExeName = "BlackGoldAncientSword.App.exe";
        private const string ProxyMirrorUrl = "https://ghproxylist.com/";

        private readonly IUpdateService _updateService;
        private readonly IClipboardService _clipboardService;

        public string LatestVersion => _updateService.LatestVersion ?? string.Empty;

        public string? DownloadUrl => _updateService.DownloadUrl;

        public bool HasDownloadUrl => !string.IsNullOrEmpty(_updateService.DownloadUrl);

        public string ReleaseNotes => _updateService.LatestReleaseNotes ?? string.Empty;

        public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(_updateService.LatestReleaseNotes);

        public bool CanOnlineUpdate =>
            (!string.IsNullOrEmpty(_updateService.ZipDownloadUrl) || !string.IsNullOrEmpty(_updateService.SplitZipDownloadUrl)) && File.Exists(UpdaterExePath);

        public string ProxyMirrorUrlText => ProxyMirrorUrl;

        public UpdateNotificationPageViewModel(IUpdateService updateService, IClipboardService clipboardService)
        {
            _updateService = updateService;
            _clipboardService = clipboardService;
            RaisePropertyChanged(nameof(LatestVersion));
            RaisePropertyChanged(nameof(DownloadUrl));
            RaisePropertyChanged(nameof(HasDownloadUrl));
            RaisePropertyChanged(nameof(ReleaseNotes));
            RaisePropertyChanged(nameof(HasReleaseNotes));
            RaisePropertyChanged(nameof(CanOnlineUpdate));
        }

        private DelegateCommand? _openDownloadCommand;
        public DelegateCommand OpenDownloadCommand =>
            _openDownloadCommand ??= new DelegateCommand(() =>
            {
                try
                {
                    var url = _updateService.DownloadUrl;
                    if (string.IsNullOrEmpty(url)) url = _updateService.ReleasePageUrl;
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    DismissOverlay();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OpenDownloadCommand)}] 浏览器打开失败: {ex}");
                }
            });

        private DelegateCommand? _copyDownloadUrlCommand;
        public DelegateCommand CopyDownloadUrlCommand =>
            _copyDownloadUrlCommand ??= new DelegateCommand(() =>
            {
                var url = _updateService.DownloadUrl;
                if (string.IsNullOrEmpty(url)) return;
                _clipboardService.TrySetText(url);
                eventAggregator.GetEvent<TipMessageEvent>().Publish(new TipMessageWithHighlightArgs("下载链接已复制到剪贴板"));
            });

        private DelegateCommand? _openProxyMirrorCommand;
        public DelegateCommand OpenProxyMirrorCommand =>
            _openProxyMirrorCommand ??= new DelegateCommand(() =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(ProxyMirrorUrl) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OpenProxyMirrorCommand)}] 打开代理站点失败: {ex}");
                }
            });

        private DelegateCommand? _onlineUpdateCommand;
        public DelegateCommand OnlineUpdateCommand =>
            _onlineUpdateCommand ??= new DelegateCommand(() =>
            {
                try
                {
                    var zipUrl = _updateService.ZipDownloadUrl;
                    if (string.IsNullOrEmpty(zipUrl))
                    {
                        Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OnlineUpdateCommand)}] 无 zip 资产");
                        return;
                    }
                    var updaterExe = UpdaterExePath;
                    if (!File.Exists(updaterExe))
                    {
                        Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OnlineUpdateCommand)}] 未找到更新程序: {updaterExe}");
                        return;
                    }

                    var targetDir = AppContext.BaseDirectory.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var psi = new ProcessStartInfo
                    {
                        FileName = updaterExe,
                        WorkingDirectory = targetDir,
                        UseShellExecute = false,
                    };
                    psi.ArgumentList.Add("--url");
                    psi.ArgumentList.Add(zipUrl);
                    var splitUrl = _updateService.SplitZipDownloadUrl;
                    if (!string.IsNullOrEmpty(splitUrl))
                    {
                        psi.ArgumentList.Add("--split-url");
                        psi.ArgumentList.Add(splitUrl);
                    }
                    psi.ArgumentList.Add("--target");
                    psi.ArgumentList.Add(targetDir);
                    psi.ArgumentList.Add("--main-exe");
                    psi.ArgumentList.Add(MainAppExeName);
                    // 传当前进程 PID 给 Updater，让其用 GetProcessById 精确定位本主程序进程，
                    // 绕开 image name 不一致（dotnet host / 同名异目录 / 多会话）等歧义。
                    psi.ArgumentList.Add("--main-pid");
                    psi.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Process.Start(psi);
                    DismissOverlay();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OnlineUpdateCommand)}] 启动在线更新失败: {ex}");
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

        private static string UpdaterExePath =>
            Path.Combine(AppContext.BaseDirectory, UpdaterExeName);
    }
}
