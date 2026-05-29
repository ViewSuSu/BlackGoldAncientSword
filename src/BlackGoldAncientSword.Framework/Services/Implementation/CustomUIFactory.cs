using System.Diagnostics;
using System.Windows;
using NetSparkleUpdater.UI.WPF;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    internal class CustomUIFactory : UIFactory
    {
        public CustomUIFactory() : base()
        {
            Debug.WriteLine("[CustomUIFactory] 无参构造函数");
        }

        public CustomUIFactory(System.Windows.Media.ImageSource icon) : base(icon)
        {
            Debug.WriteLine("[CustomUIFactory] 带图标构造函数");
        }

        public override void ShowCannotDownloadAppcast(string? appcastUrl)
        {
            Debug.WriteLine($"[CustomUIFactory] ShowCannotDownloadAppcast, URL: {appcastUrl ?? "null"}");

            var message = Application.Current?.TryFindResource("Settings.UpdateCheckFailed") as string
                ?? "检查更新失败，请稍后重试";

            var title = Application.Current?.TryFindResource("Settings.CheckForUpdates") as string
                ?? "检查更新";

            Debug.WriteLine($"[CustomUIFactory] 显示 MessageBox: title={title}, message={message}");

            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        public override void ShowDownloadErrorMessage(string message, string? appcastUrl)
        {
            Debug.WriteLine($"[CustomUIFactory] ShowDownloadErrorMessage, 原始消息: {message}, URL: {appcastUrl ?? "null"}");

            var title = Application.Current?.TryFindResource("Settings.CheckForUpdates") as string
                ?? "检查更新";

            var localizedMessage = Application.Current?.TryFindResource("Settings.UpdateCheckFailed") as string
                ?? "检查更新失败，请稍后重试";

            Debug.WriteLine($"[CustomUIFactory] 显示 MessageBox: title={title}, message={localizedMessage}");

            MessageBox.Show(
                localizedMessage,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}