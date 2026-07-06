using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels;

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
            Unloaded += OnPageUnloaded;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
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
