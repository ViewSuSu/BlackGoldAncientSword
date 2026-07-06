using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http.Auth.Captcha;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using BlackGoldAncientSword.Framework.Http.Auth.WechatQr;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels
{
    /// <summary>
    /// 登录流程状态机：<br/>
    ///   Loading → CaptchaPending → CaptchaVerifying → QrLoading → QrPolling → Success / Failed<br/>
    /// 页面 UI 通过 <see cref="IsCaptchaStage"/>/<see cref="IsQrStage"/> 决定显示哪一步。
    /// </summary>
    public class AuthChallengePageViewModel : ViewModelBase
    {
        private readonly IAjCaptchaService _captcha;
        private readonly IWechatQrLoginService _qr;
        private readonly IAuthTokenStore _tokenStore;
        private readonly IAuthTokenState _tokenState;
        private readonly IAuthChallengeService _challenge;

        private CaptchaChallenge? _currentCaptcha;
        private QrChallenge? _currentQr;
        private CancellationTokenSource? _pollCts;
        private bool _completed;

        public AuthChallengePageViewModel(
            IAjCaptchaService captcha,
            IWechatQrLoginService qr,
            IAuthTokenStore tokenStore,
            IAuthTokenState tokenState,
            IAuthChallengeService challenge)
        {
            _captcha = captcha;
            _qr = qr;
            _tokenStore = tokenStore;
            _tokenState = tokenState;
            _challenge = challenge;
            _ = LoadCaptchaAsync();
        }

        #region Bindable state

        private string _stepTitle = "请完成滑块验证";
        public string StepTitle { get => _stepTitle; set { _stepTitle = value; RaisePropertyChanged(); } }

        private string _statusText = "";
        public string StatusText { get => _statusText; set { _statusText = value; RaisePropertyChanged(); } }

        private bool _isCaptchaStage = true;
        public bool IsCaptchaStage { get => _isCaptchaStage; set { _isCaptchaStage = value; RaisePropertyChanged(); } }

        private bool _isQrStage;
        public bool IsQrStage { get => _isQrStage; set { _isQrStage = value; RaisePropertyChanged(); } }

        private bool _isCaptchaLoading;
        public bool IsCaptchaLoading { get => _isCaptchaLoading; set { _isCaptchaLoading = value; RaisePropertyChanged(); } }

        private bool _isCaptchaVerifying;
        public bool IsCaptchaVerifying { get => _isCaptchaVerifying; set { _isCaptchaVerifying = value; RaisePropertyChanged(); } }

        private string? _captchaError;
        public string? CaptchaError { get => _captchaError; set { _captchaError = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasCaptchaError)); } }
        public bool HasCaptchaError => !string.IsNullOrEmpty(_captchaError);

        private string _sliderHintText = "按住滑块向右拖动完成验证";
        public string SliderHintText { get => _sliderHintText; set { _sliderHintText = value; RaisePropertyChanged(); } }

        private BitmapImage? _backgroundImage;
        public BitmapImage? BackgroundImage { get => _backgroundImage; set { _backgroundImage = value; RaisePropertyChanged(); } }

        private BitmapImage? _jigsawImage;
        public BitmapImage? JigsawImage { get => _jigsawImage; set { _jigsawImage = value; RaisePropertyChanged(); } }

        private BitmapImage? _qrImage;
        public BitmapImage? QrImage { get => _qrImage; set { _qrImage = value; RaisePropertyChanged(); } }

        private string _verificationCode = "";
        public string VerificationCode { get => _verificationCode; set { _verificationCode = value; RaisePropertyChanged(); } }

        private string _qrStatusText = "等待扫码…";
        public string QrStatusText { get => _qrStatusText; set { _qrStatusText = value; RaisePropertyChanged(); } }

        #endregion

        #region Commands

        private DelegateCommand? _refreshCaptchaCommand;
        public DelegateCommand RefreshCaptchaCommand => _refreshCaptchaCommand ??= new DelegateCommand(async () =>
        {
            await LoadCaptchaAsync();
        });

        private DelegateCommand? _cancelCommand;
        public DelegateCommand CancelCommand => _cancelCommand ??= new DelegateCommand(() =>
        {
            _pollCts?.Cancel();
            _completed = true;
            _challenge.Complete(false);
            DismissOverlay();
        });

        #endregion

        #region Flow

        private async Task LoadCaptchaAsync()
        {
            IsCaptchaLoading = true;
            CaptchaError = null;
            SliderHintText = "验证码加载中…";
            try
            {
                var c = await _captcha.GetAsync(CancellationToken.None);
                if (c is null)
                {
                    CaptchaError = "验证码加载失败，请点击「换一张」重试";
                    return;
                }
                _currentCaptcha = c;
                BackgroundImage = DecodeBase64Image(c.OriginalImageBase64);
                JigsawImage = DecodeBase64Image(c.JigsawImageBase64);
                SliderHintText = "按住滑块向右拖动完成验证";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(LoadCaptchaAsync)}] {ex}");
                CaptchaError = "验证码加载异常：" + ex.Message;
            }
            finally
            {
                IsCaptchaLoading = false;
            }
        }

        /// <summary>Code-behind 拖到位后调此。<paramref name="thumbX"/> 是手柄左端相对轨道的像素位移；<paramref name="trackWidth"/> 是当前轨道宽度。</summary>
        public async Task SubmitCaptchaAsync(double thumbX, double trackWidth)
        {
            if (_currentCaptcha is null || IsCaptchaVerifying) return;
            if (thumbX <= 0)
            {
                CaptchaError = "请拖动滑块完成验证";
                return;
            }

            IsCaptchaVerifying = true;
            CaptchaError = null;
            SliderHintText = "验证中…";
            try
            {
                // 与网页 j() 对齐：x = thumbX * R9 / trackWidth
                var normalizedX = thumbX * _captcha.ReferenceImageWidth / trackWidth;
                var verification = await _captcha.CheckAsync(_currentCaptcha, normalizedX, CancellationToken.None);
                if (string.IsNullOrEmpty(verification))
                {
                    CaptchaError = "验证失败，请重新滑动";
                    // 800ms 后自动换一张，与网页一致
                    await Task.Delay(600);
                    await LoadCaptchaAsync();
                    return;
                }

                // 进入扫码步
                await StartQrAsync(verification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(SubmitCaptchaAsync)}] {ex}");
                CaptchaError = "验证异常：" + ex.Message;
            }
            finally
            {
                IsCaptchaVerifying = false;
            }
        }

        private async Task StartQrAsync(string captchaVerification)
        {
            StatusText = "正在获取二维码…";
            try
            {
                var qr = await _qr.CreateAsync(captchaVerification, CancellationToken.None);
                if (qr is null)
                {
                    StatusText = "获取二维码失败";
                    CaptchaError = "获取二维码失败，请重新验证";
                    IsCaptchaStage = true;
                    IsQrStage = false;
                    return;
                }

                _currentQr = qr;
                VerificationCode = qr.VerificationCode;
                QrImage = await DownloadImageAsync(qr.QrCodeUrl);
                StepTitle = "微信扫码登录";
                IsCaptchaStage = false;
                IsQrStage = true;
                StatusText = "";
                QrStatusText = "等待扫码…";

                _pollCts?.Cancel();
                _pollCts = new CancellationTokenSource();
                _ = PollLoopAsync(_pollCts.Token, qr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(StartQrAsync)}] {ex}");
                StatusText = "获取二维码异常：" + ex.Message;
            }
        }

        private async Task PollLoopAsync(CancellationToken ct, QrChallenge qr)
        {
            var interval = TimeSpan.FromMilliseconds(qr.PollIntervalMs);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _qr.PollAsync(qr.Scene, ct);
                    switch (result.Outcome)
                    {
                        case QrPollOutcome.Success when result.Token != null:
                            OnLoginSucceeded(result.Token);
                            return;
                        case QrPollOutcome.Scanned:
                            QrStatusText = "已扫描，请在微信里确认…";
                            break;
                        case QrPollOutcome.Expired:
                            QrStatusText = "二维码已过期，请返回重新验证";
                            return;
                        case QrPollOutcome.Failed:
                            QrStatusText = "轮询失败，稍后重试";
                            break;
                        default:
                            QrStatusText = "等待扫码…";
                            break;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(PollLoopAsync)}] {ex}");
                }
                try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { return; }
            }
        }

        private void OnLoginSucceeded(AuthToken token)
        {
            try
            {
                _tokenState.Set(token);
                _tokenStore.Save(token);
                _completed = true;
                _challenge.Complete(true);
                StatusText = "登录成功，正在返回…";
                DismissOverlay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(OnLoginSucceeded)}] {ex}");
                StatusText = "保存 token 失败：" + ex.Message;
            }
        }

        public void NotifyDismissedWithoutLogin()
        {
            if (_completed) return;
            _pollCts?.Cancel();
            if (_currentQr != null)
                _ = _qr.CancelAsync(_currentQr.Scene, CancellationToken.None);
            _challenge.Complete(false);
        }

        #endregion

        #region Helpers

        private void DismissOverlay()
        {
            if (regionManager.Regions.ContainsRegionWithName(GlobalConstant.AuthChallengeRegion))
                regionManager.Regions[GlobalConstant.AuthChallengeRegion].RemoveAll();
        }

        private static BitmapImage? DecodeBase64Image(string base64)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(DecodeBase64Image)}] {ex}");
                return null;
            }
        }

        private static readonly HttpClient _plainHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static async Task<BitmapImage?> DownloadImageAsync(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return null;
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = url.IndexOf(',');
                    if (idx < 0) return null;
                    return DecodeBase64Image(url.Substring(idx + 1));
                }
                // 走 HttpClient 抓字节再解码，比 BitmapImage.UriSource 更可靠（对 302 / CDN cookie / 缓存都干净）
                var bytes = await _plainHttp.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthChallengePageViewModel)}.{nameof(DownloadImageAsync)}] {ex}");
                return null;
            }
        }

        #endregion
    }
}
