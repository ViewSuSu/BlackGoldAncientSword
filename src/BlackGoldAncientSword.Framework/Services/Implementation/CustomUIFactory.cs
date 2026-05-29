using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.UI.WPF;
using NetSparkleUpdater.UI.WPF.ViewModels;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    internal class CustomUIFactory : UIFactory
    {
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

            var window = base.CreateUpdateAvailableWindow(
                updates, signatureVerifier, currentVersion, appName, isUpdateAlreadyDownloaded);

            if (window is UpdateAvailableWindow wpfWindow)
            {
                // Localize window title
                wpfWindow.Title = ResOrDefault("UpdateDialog.SoftwareUpdate", "Software Update");
                wpfWindow.Background = Brushes.White;
                ApplyHandyControlStyles(wpfWindow);

                // Localize buttons
                if (wpfWindow.FindName("SkipButton") is Button skipBtn)
                    skipBtn.Content = ResOrDefault("UpdateDialog.SkipVersion", "Skip this version");

                if (wpfWindow.FindName("RemindMeLaterButton") is Button remindBtn)
                    remindBtn.Visibility = Visibility.Collapsed;

                if (wpfWindow.FindName("DownloadInstallButton") is Button installBtn)
                {
                    installBtn.Content = isUpdateAlreadyDownloaded
                        ? ResOrDefault("UpdateDialog.Restart", "Restart")
                        : ResOrDefault("UpdateDialog.DownloadInstall", "Download/Install");
                    installBtn.MinWidth = 60;
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
                ApplyHandyControlStyles(wpfWindow);

                // Localize action button + hook download completion
                if (wpfWindow.DataContext is DownloadProgressWindowViewModel progressVm)
                {
                    // Set initial button text (Cancel or Install)
                    progressVm.ActionButtonTitle = localizedAction;

                    // Hook download completion: change title to "更新完成" and button to "重新启动"
                    progressVm.PropertyChanged += (sender, args) =>
                    {
                        if (args.PropertyName == nameof(progressVm.IsDownloading) && !progressVm.IsDownloading)
                        {
                            // Download finished successfully
                            if (progressVm.DidDownloadAnything && !progressVm.DidDownloadFail)
                            {
                                progressVm.ActionButtonTitle = ResOrDefault("UpdateDialog.Restart", "Restart");
                                // Update downloading title text
                                var tbs = FindVisualChildren<TextBlock>(wpfWindow).ToList();
                                if (tbs.Count > 0)
                                    tbs[0].Text = ResOrDefault("UpdateDialog.UpdateCompleted", "Update Complete");
                            }
                        }
                    };
                }

                // Fallback: find button in visual tree and set Content directly
                foreach (var btn in FindVisualChildren<Button>(wpfWindow))
                {
                    btn.Content = localizedAction;
                    btn.MinWidth = 60;
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
            ShowLocalizedMessage(
                ResOrDefault("UpdateDialog.InfoTitle", "Info"),
                ResOrDefault("UpdateDialog.UpToDate", "Your current version is up to date."));
        }

        public override void ShowVersionIsSkippedByUserRequest()
        {
            Debug.WriteLine("[CustomUIFactory] ShowVersionIsSkippedByUserRequest");
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
            Debug.WriteLine("[CustomUIFactory] ShowToast");
            var toastMessage = ResOrDefault("UpdateDialog.ToastMessage", "New Version Available");
            var toastAction = ResOrDefault("UpdateDialog.ToastAction", "More information");

            Thread thread = new Thread(() =>
            {
                var toast = new ToastNotification()
                {
                    ClickAction = clickHandler,
                    Icon = _applicationIcon
                };
                try
                {
                    ProcessWindowAfterInit?.Invoke(toast, this);
                    toast.Show(toastMessage, toastAction, 5);
                    System.Windows.Threading.Dispatcher.Run();
                }
                catch (ThreadAbortException)
                {
                    toast.Dispatcher.InvokeShutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        #endregion
    }
}