using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.AuthChallenge.Services;

namespace BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels
{

    public class AuthChallengePageViewModel : ViewModelBase
    {
        private readonly IHeyboxQrLoginService _qr;
        private readonly IHeyboxBrowserLoginService _browserLogin;
        private readonly IHeyboxSessionState _sessionState;
        private readonly IHeyboxSessionStore _sessionStore;
        private readonly IAuthChallengeService _challenge;
        private readonly HeyboxSelfProfileResolver _profileResolver;

        private CancellationTokenSource? _pollCts;
        private CancellationTokenSource? _browserLoginCts;
        private bool _completed;

        public AuthChallengePageViewModel(
            IHeyboxQrLoginService qr,
            IHeyboxBrowserLoginService browserLogin,
            IHeyboxSessionState sessionState,
            IHeyboxSessionStore sessionStore,
            IAuthChallengeService challenge,
            HeyboxSelfProfileResolver profileResolver)
        {
            _qr = qr;
            _browserLogin = browserLogin;
            _sessionState = sessionState;
            _sessionStore = sessionStore;
            _challenge = challenge;
            _profileResolver = profileResolver;

            if (_sessionState.Current is not null)
            {
                StatusText = "登录成功，正在返回…";
                _completed = true;
                DismissOverlay();
                return;
            }

            _ = StartQrAsync();
        }

        #region Bindable state

        private string _statusText = "";
        public string StatusText { get => _statusText; set { _statusText = value; RaisePropertyChanged(); } }

        private string _qrStatusText = "正在获取二维码…";
        public string QrStatusText { get => _qrStatusText; set { _qrStatusText = value; RaisePropertyChanged(); } }

        private BitmapImage? _qrImage;
        public BitmapImage? QrImage { get => _qrImage; set { _qrImage = value; RaisePropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; RaisePropertyChanged(); } }

        #endregion

        #region 登录方式切换

        private enum LoginMode { Qr, Browser }

        private LoginMode _mode = LoginMode.Qr;

        public bool IsQrMode => _mode == LoginMode.Qr;

        public bool IsBrowserMode => _mode == LoginMode.Browser;

        public string ModeSwitchText => IsQrMode ? "手机号登录" : "微信扫码登录";

        private string _browserStatusText = string.Empty;
        public string BrowserStatusText
        {
            get => _browserStatusText;
            private set { _browserStatusText = value; RaisePropertyChanged(); }
        }

        private DelegateCommand? _switchLoginModeCommand;
        public DelegateCommand SwitchLoginModeCommand => _switchLoginModeCommand ??= new DelegateCommand(SwitchLoginMode);

        private DelegateCommand? _retryBrowserLoginCommand;
        public DelegateCommand RetryBrowserLoginCommand => _retryBrowserLoginCommand ??= new DelegateCommand(StartBrowserLogin);

        private void SwitchLoginMode()
        {
            if (_completed) return;

            if (_mode == LoginMode.Qr)
            {
                _pollCts?.Cancel();
                SetMode(LoginMode.Browser);
                StartBrowserLogin();
                return;
            }

            CancelBrowserLogin();
            SetMode(LoginMode.Qr);
            _ = StartQrAsync();
        }

        private void SetMode(LoginMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            RaisePropertyChanged(nameof(IsQrMode));
            RaisePropertyChanged(nameof(IsBrowserMode));
            RaisePropertyChanged(nameof(ModeSwitchText));
        }

        private void StartBrowserLogin()
        {
            if (_completed || _browserLoginCts is not null) return;

            _browserLoginCts = new CancellationTokenSource();
            _ = RunBrowserLoginAsync(_browserLoginCts);
        }

        private async Task RunBrowserLoginAsync(CancellationTokenSource cts)
        {
            BrowserStatusText = "已打开浏览器，请在小黑盒登录页里完成登录（手机号或微信都可以）…";
            try
            {
                var session = await _browserLogin.LoginAsync(cts.Token).ConfigureAwait(true);
                if (session is null)
                {
                    if (!cts.IsCancellationRequested)
                        BrowserStatusText = "还没等到登录完成。可以点「重新打开登录页」再试。";
                    return;
                }

                await OnLoginSucceededAsync(session).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(AuthChallengePageViewModel)}.{nameof(RunBrowserLoginAsync)}");
                BrowserStatusText = "登录失败：" + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_browserLoginCts, cts)) _browserLoginCts = null;
                cts.Dispose();
            }
        }

        private void CancelBrowserLogin()
        {
            var cts = _browserLoginCts;
            _browserLoginCts = null;
            if (cts is null) return;

            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #region Commands

        private DelegateCommand? _refreshQrCommand;
        public DelegateCommand RefreshQrCommand => _refreshQrCommand ??= new DelegateCommand(async () =>
        {
            if (_completed || IsBusy) return;
            await StartQrAsync();
        });

        private DelegateCommand? _cancelCommand;
        public DelegateCommand CancelCommand => _cancelCommand ??= new DelegateCommand(() =>
        {
            _pollCts?.Cancel();
            CancelBrowserLogin();
            _completed = true;
            _challenge.Complete(false);
            DismissOverlay();
        });

        #endregion

        #region Flow

        private async Task StartQrAsync()
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            var ct = _pollCts.Token;

            IsBusy = true;
            QrStatusText = "正在获取二维码…";
            StatusText = "";
            try
            {
                var challenge = await _qr.CreateAsync(ct).ConfigureAwait(true);
                if (challenge is null)
                {
                    AppLog.Warning($"{nameof(AuthChallengePageViewModel)}.{nameof(StartQrAsync)}", "qr create returned null");
                    QrStatusText = "获取二维码失败，请点击「刷新二维码」重试";
                    return;
                }

                QrImage = DecodeImage(challenge.ImageBytes);
                QrStatusText = "等待扫码…";
                StatusText = "";
                _ = PollLoopAsync(ct, challenge.Uuid);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(AuthChallengePageViewModel)}.{nameof(StartQrAsync)}");
                QrStatusText = "获取二维码异常：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PollLoopAsync(CancellationToken ct, string uuid)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _qr.PollAsync(uuid, ct).ConfigureAwait(true);
                    switch (result.Outcome)
                    {
                        case HeyboxQrOutcome.Success when result.Session is not null:
                            await OnLoginSucceededAsync(result.Session).ConfigureAwait(true);
                            return;
                        case HeyboxQrOutcome.Scanned:
                            QrStatusText = "已扫描，请在微信里确认…";
                            break;
                        case HeyboxQrOutcome.Expired:
                            QrStatusText = "二维码已过期，请点击「刷新二维码」";
                            return;
                        case HeyboxQrOutcome.Failed:
                            break;
                        default:
                            QrStatusText = "等待扫码…";
                            break;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"{nameof(AuthChallengePageViewModel)}.{nameof(PollLoopAsync)}");
                }

                try { await Task.Delay(1500, ct).ConfigureAwait(true); } catch (OperationCanceledException) { return; }
            }
        }

        private async Task OnLoginSucceededAsync(HeyboxSession session)
        {
            try
            {
                AppLog.Info($"{nameof(AuthChallengePageViewModel)}.{nameof(OnLoginSucceededAsync)}", "heybox qr login succeeded");
                QrStatusText = "登录成功，正在读取账号信息…";

                _sessionState.Set(HeyboxLoginState.FromSession(session));

                var (nickname, avatar) = await _profileResolver
                    .ResolveAsync(CancellationToken.None).ConfigureAwait(true);
                var loginState = new HeyboxLoginState(session, nickname, avatar);
                _sessionState.Set(loginState);
                _sessionStore.Save(session);

                _completed = true;
                _challenge.Complete(true);
                StatusText = "登录成功，正在返回…";
                DismissOverlay();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(AuthChallengePageViewModel)}.{nameof(OnLoginSucceededAsync)}", "保存登录态失败");
                StatusText = "保存登录态失败：" + ex.Message;
            }
        }

        public void NotifyDismissedWithoutLogin()
        {
            if (_completed) return;
            _pollCts?.Cancel();
            CancelBrowserLogin();
            _challenge.Complete(false);
        }
        #endregion

        #region Helpers

        private void DismissOverlay()
        {
            if (regionManager.Regions.ContainsRegionWithName(GlobalConstant.AuthChallengeRegion))
                regionManager.Regions[GlobalConstant.AuthChallengeRegion].RemoveAll();
        }

        private static BitmapImage? DecodeImage(byte[] bytes)
        {
            try
            {
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
                AppLog.Error(ex, $"{nameof(AuthChallengePageViewModel)}.{nameof(DecodeImage)}");
                return null;
            }
        }

        #endregion
    }
}
