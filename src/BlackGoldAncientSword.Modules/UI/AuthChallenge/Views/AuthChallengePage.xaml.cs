using System.Windows;
using BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels;
using HCMessageBox = HandyControl.Controls.MessageBox;

namespace BlackGoldAncientSword.Modules.UI.AuthChallenge.Views
{
    public partial class AuthChallengePage
    {
        /// <summary>用户已确认"退出程序"：后续 Unloaded 不能再按"取消登录"处理。</summary>
        private bool _exitConfirmed;

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
            {
                e.Cancel = true;
                return;
            }

            // 点「是」= 用户要的就是"关掉这个程序"，直接强杀进程。
            // 不能只放行关闭让浮层卸载、等 App.OnStartup 的登录 gate 收到取消后再走
            // Application.Shutdown()：那条路径要跨线程续接，会在关窗/清理阶段抛
            // "调用线程无法访问此对象"（弹窗挡在用户面前、程序也退不掉）。
            _exitConfirmed = true;
            (DataContext as AuthChallengePageViewModel)?.NotifyExitConfirmed();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            // 已确认退出时进程正在被强杀，这里不再重复走"取消登录"分支。
            if (_exitConfirmed) return;
            if (DataContext is AuthChallengePageViewModel vm) vm.NotifyDismissedWithoutLogin();
        }
    }
}
