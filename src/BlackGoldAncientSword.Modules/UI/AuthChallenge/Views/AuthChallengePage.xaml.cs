using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels;
using HCMessageBox = HandyControl.Controls.MessageBox;

namespace BlackGoldAncientSword.Modules.UI.AuthChallenge.Views
{
    public partial class AuthChallengePage
    {
        // 与网页 R9=310（滑块参考底图宽度）、z9=44（手柄宽度）一致
        private const double TrackWidth = 310;
        private const double ThumbWidth = 44;
        private const double MaxThumbX = TrackWidth - ThumbWidth;

        private bool _dragging;
        private double _startMouseX;
        private double _startThumbX;

        public AuthChallengePage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
            DataContextChanged += OnDataContextChanged;
            ChallengeOverlay.Closing += OnOverlayClosing;
        }

        /// <summary>
        /// 右上角 X 点击：登录界面属于强门槛（未完成 = App 不可用），关闭 = 退出程序。
        /// 弹二次确认，避免误触。用户确认后走原始 Dismiss 流程：
        /// OverlayHost.Dismiss → region.RemoveAll → OnPageUnloaded → NotifyDismissedWithoutLogin
        /// → Complete(false) → App.OnStartup [5] 拿到 loggedIn=false → Shutdown。
        /// </summary>
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

        private AuthChallengePageViewModel? _subscribedVm;

        private void OnPageLoaded(object sender, RoutedEventArgs e) => SubscribeVm(DataContext as AuthChallengePageViewModel);

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e) => SubscribeVm(e.NewValue as AuthChallengePageViewModel);

        private void SubscribeVm(AuthChallengePageViewModel? vm)
        {
            if (ReferenceEquals(_subscribedVm, vm)) return;
            if (_subscribedVm is not null) _subscribedVm.CaptchaReloadStarted -= ResetSliderVisual;
            _subscribedVm = vm;
            if (_subscribedVm is not null) _subscribedVm.CaptchaReloadStarted += ResetSliderVisual;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (_subscribedVm is not null)
            {
                _subscribedVm.CaptchaReloadStarted -= ResetSliderVisual;
                _subscribedVm = null;
            }
            if (DataContext is AuthChallengePageViewModel vm) vm.NotifyDismissedWithoutLogin();
        }

        private void OnSliderThumbMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not AuthChallengePageViewModel vm || !vm.IsCaptchaStage) return;
            if (vm.IsCaptchaLoading || vm.IsCaptchaVerifying) return;

            _dragging = true;
            _startMouseX = e.GetPosition(SliderTrack).X;
            _startThumbX = ThumbTranslate.X;

            SliderThumb.CaptureMouse();
            SliderThumb.MouseMove += OnSliderThumbMouseMove;
            SliderThumb.MouseLeftButtonUp += OnSliderThumbMouseUp;
            SliderThumb.LostMouseCapture += OnSliderThumbLostCapture;
        }

        private void OnSliderThumbMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var current = e.GetPosition(SliderTrack).X;
            var next = _startThumbX + (current - _startMouseX);
            if (next < 0) next = 0;
            if (next > MaxThumbX) next = MaxThumbX;
            ThumbTranslate.X = next;
            JigsawTranslate.X = next;
            SliderProgress.Width = next + ThumbWidth;
        }

        private async void OnSliderThumbMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            EndDrag();
            if (DataContext is AuthChallengePageViewModel vm)
                await vm.SubmitCaptchaAsync(ThumbTranslate.X, TrackWidth);
        }

        private void OnSliderThumbLostCapture(object sender, MouseEventArgs e) => EndDrag();

        private void EndDrag()
        {
            _dragging = false;
            SliderThumb.ReleaseMouseCapture();
            SliderThumb.MouseMove -= OnSliderThumbMouseMove;
            SliderThumb.MouseLeftButtonUp -= OnSliderThumbMouseUp;
            SliderThumb.LostMouseCapture -= OnSliderThumbLostCapture;
        }

        public void ResetSliderVisual()
        {
            ThumbTranslate.X = 0;
            JigsawTranslate.X = 0;
            SliderProgress.Width = 0;
        }
    }
}
