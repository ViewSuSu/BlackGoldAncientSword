using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.UI.WPF;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.UI.WPF.ViewModels;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    public class CustomUIFactory : UIFactory
    {
        public static bool SuppressDialogs { get; set; }
        public static bool ShowNoUpdateMessage { get; set; } = true;
        public CustomUIFactory() : base()
        {
            Debug.WriteLine("[CustomUIFactory] 无参构造函数");
            InitWhiteBackground();
        }

        public CustomUIFactory(ImageSource icon) : base(icon)
        {
            Debug.WriteLine("[CustomUIFactory] 带图标构造函数");
            InitWhiteBackground();
        }

        private static string GetInstalledVersion()
        {
            var attr = typeof(CustomUIFactory).Assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                var version = attr.InformationalVersion;
                var plusIndex = version.IndexOf('+');
                return plusIndex > 0 ? version[..plusIndex] : version;
            }
            return "";
        }

        #region Resource Helpers

        private static string? Res(string key)
            => Application.Current?.TryFindResource(key) as string;

        private static string ResOrDefault(string key, string fallback)
            => Res(key) ?? fallback;

        private void InitWhiteBackground()
        {
            UseStaticUpdateWindowBackgroundColor = true;
            var whiteBrush = new SolidColorBrush(Colors.White);
            whiteBrush.Freeze();
            UpdateWindowGridBackgroundBrush = whiteBrush;
            HideReleaseNotes = true;
            HideSkipButton = true;
        }

        private static void ApplyHandyControlStyles(Window window)
        {
            // Apply HandyControl ProgressBar style only, without loading full theme
            var hcStyle = Application.Current?.TryFindResource(typeof(ProgressBar)) as Style;
            if (hcStyle == null) return;

            foreach (var pb in FindVisualChildren<ProgressBar>(window))
            {
                pb.Style = hcStyle;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var grandChild in FindVisualChildren<T>(child))
                    yield return grandChild;
            }
        }

        #endregion

        #region CreateUpdateAvailableWindow

        public override IUpdateAvailable CreateUpdateAvailableWindow(
            List<AppCastItem> updates,
            ISignatureVerifier? signatureVerifier,
            string currentVersion,
            string appName,
            bool isUpdateAlreadyDownloaded)
        {
            Debug.WriteLine("[CustomUIFactory] CreateUpdateAvailableWindow 开始");

            var latestItem = updates.FirstOrDefault();
            var latestVersion = latestItem?.Version ?? "";
            var installedVersion = GetInstalledVersion();
            var isSameVersion = !string.IsNullOrEmpty(latestVersion) &&
                string.Equals(latestVersion, installedVersion, StringComparison.OrdinalIgnoreCase);

            if (SuppressDialogs)
            {
                Debug.WriteLine("[CustomUIFactory] SuppressDialogs=true?????");
                return new SuppressedUpdateAvailable();
            }

            var window = base.CreateUpdateAvailableWindow(
                updates, signatureVerifier, currentVersion, appName, isUpdateAlreadyDownloaded);

            if (window is UpdateAvailableWindow wpfWindow)
            {
                // Localize window title
                wpfWindow.Title = ResOrDefault("UpdateDialog.SoftwareUpdate", "Software Update");
                wpfWindow.Background = Brushes.White;
                wpfWindow.Owner = Application.Current.MainWindow;
                wpfWindow.Topmost = true;
                ApplyHandyControlStyles(wpfWindow);

                // Localize buttons
                if (wpfWindow.FindName("SkipButton") is Button skipBtn)
                {
                    skipBtn.Content = ResOrDefault("UpdateDialog.SkipVersion", "Skip this version");
                    if (isSameVersion)
                        skipBtn.Visibility = Visibility.Collapsed;
                }

                if (wpfWindow.FindName("RemindMeLaterButton") is Button remindBtn)
                    remindBtn.Visibility = Visibility.Collapsed;

                if (wpfWindow.FindName("DownloadInstallButton") is Button installBtn)
                {
                    if (isSameVersion)
                    {
                        installBtn.Content = ResOrDefault("UpdateDialog.UpToDate", "Up to Date");
                        installBtn.IsEnabled = false;
                        installBtn.MinWidth = 60;
                    }
                    else
                    {
                        installBtn.Content = isUpdateAlreadyDownloaded
                            ? ResOrDefault("UpdateDialog.Restart", "Restart")
                            : ResOrDefault("UpdateDialog.DownloadInstall", "Download/Install");
                        installBtn.MinWidth = 60;
                    }
                }

                // Localize view model strings
                if (wpfWindow.DataContext is UpdateAvailableWindowViewModel vm)
                {
                    var item = updates.FirstOrDefault();
                    var downloadInstallWord = isUpdateAlreadyDownloaded
                        ? ResOrDefault("UpdateDialog.Restart", "Restart")
                        : ResOrDefault("UpdateDialog.Download", "update");

                    vm.TitleHeaderText = ResOrDefault("UpdateDialog.NewVersionAvailable",
                        "A new version is available.");

                    if (item != null)
                    {
                        var versionString = currentVersion ?? "";
                        if (string.IsNullOrWhiteSpace(versionString))
                        {
                            try
                            {
                                var itemVersion = item.Version ?? "0.0"; var versionObj = new System.Version(itemVersion);
                                versionString = NetSparkleUpdater.Utilities.GetVersionString(versionObj);
                            }
                            catch { versionString = "?"; }
                        }

                        vm.InfoText = string.Format(
                            ResOrDefault("UpdateDialog.VersionInfo",
                                "{0} is now available (you have {1}). Would you like to {2} it now?"),
                            item.Version, versionString, downloadInstallWord);
                    }
                    else
                    {
                        vm.InfoText = string.Format(
                            ResOrDefault("UpdateDialog.VersionInfoNoName",
                                "Would you like to {0} it now?"),
                            downloadInstallWord);
                    }

                    // If same version, override content to show "up to date"
                    if (isSameVersion)
                    {
                        vm.TitleHeaderText = ResOrDefault("UpdateDialog.UpToDate", "Your current version is up to date.");
                        vm.InfoText = "";
                    }
                }
            }

            Debug.WriteLine("[CustomUIFactory] CreateUpdateAvailableWindow 已完成本地化");
            return window;
        }

        #endregion

        #region CreateProgressWindow

        public override IDownloadProgress CreateProgressWindow(
            string downloadTitle,
            string actionButtonTitle)
        {
            Debug.WriteLine("[CustomUIFactory] CreateProgressWindow 开始");

            var localizedTitle = ResOrDefault("UpdateDialog.DownloadingGeneric", downloadTitle);
            var localizedAction = actionButtonTitle == "Cancel"
                ? ResOrDefault("UpdateDialog.Cancel", "Cancel")
                : ResOrDefault("UpdateDialog.Install", actionButtonTitle);

            var window = base.CreateProgressWindow(localizedTitle, localizedAction);

            if (window is DownloadProgressWindow wpfWindow)
            {
                wpfWindow.Title = ResOrDefault("UpdateDialog.SoftwareUpdate", "Software Update");
                wpfWindow.Background = Brushes.White;
                wpfWindow.Owner = Application.Current.MainWindow;
                wpfWindow.Topmost = true;
                ApplyHandyControlStyles(wpfWindow);

                // Localize action button via view model
                if (wpfWindow.DataContext is DownloadProgressWindowViewModel progressVm)
                {
                    progressVm.ActionButtonTitle = localizedAction;
                }

                // Fallback: find button in visual tree and set Content directly
                foreach (var btn in FindVisualChildren<Button>(wpfWindow))
                {
                    btn.Content = localizedAction;
                    btn.MinWidth = 60;
                    btn.FontSize = 12;
                }

                // Adjust font sizes in progress window
                var textBlocks = FindVisualChildren<TextBlock>(wpfWindow).ToList();
                for (int i = 0; i < textBlocks.Count; i++)
                {
                    textBlocks[i].FontSize = i == 0 ? 15 : 12;
                }
            }

            Debug.WriteLine("[CustomUIFactory] CreateProgressWindow 已完成本地化");
            return window;
        }

        #endregion

        #region ShowCheckingForUpdates

        public override ICheckingForUpdates ShowCheckingForUpdates()
        {
            Debug.WriteLine("[CustomUIFactory] ShowCheckingForUpdates 开始");

            var window = base.ShowCheckingForUpdates();

            if (window is CheckingForUpdatesWindow checkingWindow)
            {
                checkingWindow.Title = ResOrDefault("UpdateDialog.SoftwareUpdate", "Software Update");
                checkingWindow.Background = Brushes.White;
                ApplyHandyControlStyles(checkingWindow);

                // Adjust progress bar height
                foreach (var pb in FindVisualChildren<ProgressBar>(checkingWindow))
                {
                    pb.Height = 25;
                }

                // Find TextBlock and Button via visual tree traversal
                if (checkingWindow.Content is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBlock tb && tb.FontSize >= 20)
                        {
                            tb.Text = ResOrDefault("UpdateDialog.CheckingForUpdates", "Checking for Updates...");
                        }
                        else if (child is Button cancelBtn)
                        {
                            cancelBtn.Content = ResOrDefault("UpdateDialog.Cancel", "Cancel");
                            cancelBtn.MinWidth = 100;
                            cancelBtn.MinHeight = 36;
                        }
                    }
                }
            }

            Debug.WriteLine("[CustomUIFactory] ShowCheckingForUpdates 已完成本地化");
            return window;
        }

        #endregion

        #region Message Dialogs

        public override void ShowVersionIsUpToDate()
        {
            Debug.WriteLine("[CustomUIFactory] ShowVersionIsUpToDate");
            if (!ShowNoUpdateMessage) return;
            ShowLocalizedMessage(
                ResOrDefault("UpdateDialog.InfoTitle", "Info"),
                ResOrDefault("UpdateDialog.UpToDate", "Your current version is up to date."));
        }

        public override void ShowVersionIsSkippedByUserRequest()
        {
            Debug.WriteLine("[CustomUIFactory] ShowVersionIsSkippedByUserRequest");
            if (!ShowNoUpdateMessage) return;
            ShowLocalizedMessage(
                ResOrDefault("UpdateDialog.InfoTitle", "Info"),
                ResOrDefault("UpdateDialog.VersionSkipped", "You have elected to skip this version."));
        }

        public override void ShowUnknownInstallerFormatMessage(string downloadFileName)
        {
            Debug.WriteLine($"[CustomUIFactory] ShowUnknownInstallerFormatMessage, file: {downloadFileName}");
            var message = string.Format(
                ResOrDefault("UpdateDialog.UnknownInstallerFormat",
                    "Updater not supported, please execute {0} manually."),
                downloadFileName);
            ShowLocalizedMessage(
                ResOrDefault("UpdateDialog.ErrorTitle", "Error"),
                message);
        }

        public override void ShowCannotDownloadAppcast(string? appcastUrl)
        {
            Debug.WriteLine($"[CustomUIFactory] ShowCannotDownloadAppcast, URL: {appcastUrl ?? "null"}");
            var message = ResOrDefault("UpdateDialog.CannotDownloadAppcast",
                "Unable to connect to update server. Please check your network and try again.");
            var title = ResOrDefault("UpdateDialog.ErrorTitle", "Error");
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public override void ShowDownloadErrorMessage(string message, string? appcastUrl)
        {
            Debug.WriteLine($"[CustomUIFactory] ShowDownloadErrorMessage, 原始消息: {message}, URL: {appcastUrl ?? "null"}");
            var title = ResOrDefault("UpdateDialog.ErrorTitle", "Error");
            var localizedMessage = string.Format(
                ResOrDefault("UpdateDialog.DownloadError",
                    "There was a problem downloading the update:\n{0}"),
                message);
            MessageBox.Show(localizedMessage, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ShowLocalizedMessage(string title, string message)
        {
            var messageWindow = new MessageNotificationWindow(
                new MessageNotificationWindowViewModel(message))
            {
                Title = title,
                Icon = _applicationIcon
            };
            messageWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ProcessWindowAfterInit?.Invoke(messageWindow, this);
            messageWindow.ShowDialog();
        }

        #endregion

        #region ShowToast

        public override void ShowToast(System.Action clickHandler)
        {
            // ??????Toast,??????????
        }

        #endregion
    }

    /// <summary>
    /// "自定义 UI 接管" 模式下的 <see cref="IUpdateAvailable"/> 占位实现。
    /// <para>
    /// 本项目通过 <c>SparkleUpdater.CheckForUpdatesQuietly()</c> 静默检查更新，
    /// 由 <c>UpdateService</c> 监听 <c>UpdateDetected</c> / <c>UpdateCheckFinished</c> 事件，
    /// 自行维护 <c>IsUpdateAvailable</c> 状态并通过 <c>UpdateAvailabilityChanged</c>
    /// 通知 <c>MainWindowViewModel</c>，由 XAML 绑定渲染更新提示。
    /// </para>
    /// <para>
    /// 因此 NetSparkle 在静默模式下既<b>不会调用</b> <see cref="Show"/>，
    /// 也<b>不会订阅</b> <see cref="UserResponded"/>、不会读取 <see cref="CurrentItem"/>。
    /// 当 <see cref="CustomUIFactory.SuppressDialogs"/> 为 <c>true</c> 时，
    /// <see cref="CustomUIFactory.CreateUpdateAvailableWindow"/> 返回本占位以避免
    /// NetSparkle 原生窗口被构造出来（即使框架在异常路径中尝试创建）。
    /// </para>
    /// <para>
    /// 所有成员均为空实现 / 吞订阅；<see cref="CurrentItem"/> 因接口签名为非 nullable
    /// 故保留 <c>null!</c> —— 它不会被 NetSparkle 读取。
    /// </para>
    /// </summary>
    internal sealed class SuppressedUpdateAvailable : IUpdateAvailable
    {
        public bool Displayed { get; set; }
        public NetSparkleUpdater.Enums.UpdateAvailableResult Result { get; set; }

        /// <summary>占位字段：NetSparkle 在静默模式下不会读取，仅为满足接口签名。</summary>
        public AppCastItem CurrentItem { get; set; } = null!;

        /// <summary>占位事件：静默模式下 NetSparkle 不会订阅，订阅者也不会被回调。</summary>
        public event UserRespondedToUpdate? UserResponded { add { } remove { } }

        /// <summary>占位：静默模式下 NetSparkle 不会调用 Show，UI 由 MainWindowViewModel 驱动。</summary>
        public void Show() { }

        /// <summary>占位：无对应原生窗口需要关闭。</summary>
        public void Close() { }

        /// <summary>占位：无 ReleaseNotes 控件。</summary>
        public void HideReleaseNotes() { }

        /// <summary>占位：无 RemindMeLater 按钮。</summary>
        public void HideRemindMeLaterButton() { }

        /// <summary>占位：无 Skip 按钮。</summary>
        public void HideSkipButton() { }

        /// <summary>占位：无原生窗口需要置顶。</summary>
        public void BringToFront() { }
    }
}