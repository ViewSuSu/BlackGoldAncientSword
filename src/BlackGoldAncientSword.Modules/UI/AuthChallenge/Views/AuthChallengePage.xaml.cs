using System.Windows;
using BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels;
using HCMessageBox = HandyControl.Controls.MessageBox;

namespace BlackGoldAncientSword.Modules.UI.AuthChallenge.Views
{
    public partial class AuthChallengePage
    {
        public AuthChallengePage()
        {
            InitializeComponent();
            Unloaded += OnPageUnloaded;
            ChallengeOverlay.Closing += OnOverlayClosing;
        }

        private void OnOverlayClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var result = HCMessageBox.Show(
                "取消登录将关闭程序，是否要退出程序？",
                "确认退出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                e.Cancel = true;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AuthChallengePageViewModel vm) vm.NotifyDismissedWithoutLogin();
        }
    }
}
