using System.Windows;
using NetSparkleUpdater.UI.WPF;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    internal class CustomUIFactory : UIFactory
    {
        public CustomUIFactory() : base()
        {
        }

        public CustomUIFactory(System.Windows.Media.ImageSource icon) : base(icon)
        {
        }

        public override void ShowCannotDownloadAppcast(string? appcastUrl)
        {
            var message = Application.Current?.TryFindResource("Settings.UpdateCheckFailed") as string
                ?? "检查更新失败，请稍后重试";

            var title = Application.Current?.TryFindResource("Settings.CheckForUpdates") as string
                ?? "检查更新";

            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        public override void ShowDownloadErrorMessage(string message, string? appcastUrl)
        {
            var title = Application.Current?.TryFindResource("Settings.CheckForUpdates") as string
                ?? "检查更新";

            var localizedMessage = Application.Current?.TryFindResource("Settings.UpdateCheckFailed") as string
                ?? "检查更新失败，请稍后重试";

            MessageBox.Show(
                localizedMessage,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

