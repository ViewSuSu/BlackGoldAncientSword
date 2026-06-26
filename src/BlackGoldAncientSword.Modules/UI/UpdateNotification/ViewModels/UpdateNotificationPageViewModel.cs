using System;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.UpdateNotification.ViewModels
{
    public class UpdateNotificationPageViewModel : ViewModelBase
    {
        private readonly IUpdateService _updateService;

        public string LatestVersion => _updateService.LatestVersion ?? string.Empty;

        public UpdateNotificationPageViewModel(IUpdateService updateService)
        {
            _updateService = updateService;
            RaisePropertyChanged(nameof(LatestVersion));
        }

        private DelegateCommand? _openDownloadCommand;
        public DelegateCommand OpenDownloadCommand =>
            _openDownloadCommand ??= new DelegateCommand(() =>
            {
                try
                {
                    // 主按钮跳 Release 页：用户能看 release notes、选 zip 或 installer
                    Process.Start(new ProcessStartInfo(_updateService.ReleasePageUrl) { UseShellExecute = true });
                    DismissOverlay();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.WriteLine($"[{nameof(UpdateNotificationPageViewModel)}.{nameof(OpenDownloadCommand)}] 打开 Release 页失败: {ex}");
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
