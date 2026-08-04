using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.UpdateNotification.ViewModels
{
    public class UpdateNotificationPageViewModel : ViewModelBase
    {
        private const string UpdaterExeName = "BlackGoldAncientSword.Update.exe";
        private const string MainAppExeName = "BlackGoldAncientSword.App.exe";

        private readonly IUpdateService _updateService;
        private readonly IUpdateGateService _updateGate;

        public string LatestVersion => _updateService.LatestVersion ?? string.Empty;

        public string? DownloadUrl => _updateService.DownloadUrl;

        public bool HasDownloadUrl => !string.IsNullOrEmpty(_updateService.DownloadUrl);

        public string ReleaseNotes => _updateService.LatestReleaseNotes ?? string.Empty;

        public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(_updateService.LatestReleaseNotes);

        public bool CanOnlineUpdate =>
            (!string.IsNullOrEmpty(_updateService.ZipDownloadUrl) || (_updateService.SplitDownloadUrls is { Count: > 0 })) && File.Exists(UpdaterExePath);

        public UpdateNotificationPageViewModel(IUpdateService updateService, IUpdateGateService updateGate)
        {
            _updateService = updateService;
            _updateGate = updateGate;
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
                    AppLog.Error(ex, $"{nameof(UpdateNotificationPageViewModel)}.{nameof(OpenDownloadCommand)}", "浏览器打开失败");
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
                    var splitUrls = _updateService.SplitDownloadUrls;
                    if (splitUrls is { Count: > 0 })
                    {
                        foreach (var u in splitUrls)
                        {
                            psi.ArgumentList.Add("--split-url");
                            psi.ArgumentList.Add(u);
                        }

                    }
                    psi.ArgumentList.Add("--target");
                    psi.ArgumentList.Add(targetDir);
                    psi.ArgumentList.Add("--main-exe");
                    psi.ArgumentList.Add(MainAppExeName);
                    // 传当前进程 PID 给 Updater，让其用 GetProcessById 精确定位本主程序进程，
                    // 绕开 image name 不一致（dotnet host / 同名异目录 / 多会话）等歧义。
                    psi.ArgumentList.Add("--main-pid");
                    psi.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    // 传主窗口 HWND，让 Updater 更新窗口精确居中到主 App 窗口——
                    // 主 App 是 AllowsTransparency+WindowStyle=None，Process.MainWindowHandle 有时拿不到，
                    // 直接传 hwnd 是最稳的路径。
                    var mainHwnd = new System.Windows.Interop.WindowInteropHelper(
                        System.Windows.Application.Current.MainWindow).Handle;
                    if (mainHwnd != IntPtr.Zero)
                    {
                        psi.ArgumentList.Add("--main-hwnd");
                        psi.ArgumentList.Add(mainHwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    var updaterProc = Process.Start(psi);
                    // 拉起 Updater 后：移除更新卡片 + 通知主窗口进入"锁死等待重启"状态。
                    // 特意不调 _updateGate.Complete()——App.OnStartup [4] 保持挂起，用户不会走到 [5] 登录 gate
                    // 与 [6] 主页导航，主窗口只剩顶层遮罩，直到 Updater 装完后 kill 本进程重启新版。
                    var region = regionManager.Regions[GlobalConstant.UpdateNotificationRegion];
                    region.RemoveAll();
                    eventAggregator.GetEvent<OnlineUpdatingStartedEvent>().Publish();

                    // 后台监视 Updater 进程：若 Updater 提前退出（用户在 Updater 里点取消 / 下载失败 / 崩溃）
                    // 则主 App 需要从"锁死"状态恢复——正常路径下 Updater 完成后会 kill 主 App，主 App 直接进程死掉，
                    // 这个 continuation 也不会有机会跑；只有异常路径才会命中恢复逻辑。
                    if (updaterProc != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await updaterProc.WaitForExitAsync().ConfigureAwait(false); }
                            catch { /* 忽略：exit 时序异常不影响恢复逻辑 */ }
                            finally { try { updaterProc.Dispose(); } catch { } }
                            eventAggregator.GetEvent<OnlineUpdatingCancelledEvent>().Publish();
                        });
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Error(ex, $"{nameof(UpdateNotificationPageViewModel)}.{nameof(OnlineUpdateCommand)}", "启动在线更新失败");
                }
            });

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(DismissOverlay);

        private void DismissOverlay()
        {
            var region = regionManager.Regions[GlobalConstant.UpdateNotificationRegion];
            region.RemoveAll();
            // 唤醒 App.OnStartup 里 await 的 UpdateGate；用户手动"检查更新"复用同一 VM，此时 gate 已 Complete
            // 过（TCS 为 null），Complete() 是幂等 no-op，不会误触发第二次登录 gate。
            _updateGate.Complete();
        }

        private static string UpdaterExePath =>
            Path.Combine(AppContext.BaseDirectory, UpdaterExeName);
    }
}
