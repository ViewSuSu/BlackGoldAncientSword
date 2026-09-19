using System.Windows;
using BlackGoldAncientSword.Modules.UI.UpdateNotification.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.UpdateNotification.Views
{
    public partial class UpdateNotificationPage
    {
        public UpdateNotificationPage()
        {
            InitializeComponent();
            // 三条关闭路径（右上角 × / Esc / region.RemoveAll）最终都会让本视图 Unloaded，
            // 在这里统一释放更新 gate；OverlayHost 自己只清 Region，不会碰 gate。
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            (DataContext as UpdateNotificationPageViewModel)?.OnOverlayClosed();
        }
    }
}
