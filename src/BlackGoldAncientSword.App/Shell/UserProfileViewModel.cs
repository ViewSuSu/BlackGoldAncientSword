using System;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.App.Shell
{
    /// <summary>
    /// 标题栏用户头像按钮 + 停靠 Popup 的 VM。与网页版右上角 <c>user-menu-trigger</c> 行为一致：
    /// 未登录 → 按钮显示"登录" icon + 文本，点击弹 QR 扫码；
    /// 已登录 → 按钮显示圆形头像 + "登录成功"文案（不显示昵称，昵称放 ToolTip 与 Popup 里），
    /// 点击弹 Popup（头像 + 昵称 + 退出登录）。
    /// 单例：跟随 <see cref="IHeyboxSessionState.Changed"/> 事件在整个 App 生命周期内反应用户登录状态。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class UserProfileViewModel : ViewModelBase
    {
        private const string DefaultAvatarPackUri =
            "pack://application:,,,/BlackGoldAncientSword.Resources;component/Images/Avatar/avatar_default.png";

        private readonly IHeyboxSessionState _sessionState;
        private readonly IHeyboxSessionStore _sessionStore;
        private readonly IAuthChallengeService _challenge;
        private readonly IUIDispatcher _uiDispatcher;

        private bool _isLoggedIn;
        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            private set
            {
                if (_isLoggedIn == value) return;
                _isLoggedIn = value;
                RaisePropertyChanged();
            }
        }

        private string _nickname = string.Empty;
        public string Nickname
        {
            get => _nickname;
            private set
            {
                if (_nickname == value) return;
                _nickname = value;
                RaisePropertyChanged();
            }
        }

        private string _avatarUrl = DefaultAvatarPackUri;
        /// <summary>
        /// 未登录 or 用户无自定义头像 → 本地打包的 <c>avatar_default.png</c>；
        /// 已登录且服务器返回 avatar 字段 → 直接使用远程 URL（WPF Image 控件支持 http/https）。
        /// </summary>
        public string AvatarUrl
        {
            get => _avatarUrl;
            private set
            {
                if (_avatarUrl == value) return;
                _avatarUrl = value;
                RaisePropertyChanged();
            }
        }

        private bool _isPopupOpen;
        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set
            {
                if (_isPopupOpen == value) return;
                _isPopupOpen = value;
                RaisePropertyChanged();
            }
        }

        public UserProfileViewModel(
            IHeyboxSessionState sessionState,
            IHeyboxSessionStore sessionStore,
            IAuthChallengeService challenge,
            IUIDispatcher uiDispatcher)
        {
            _sessionState = sessionState;
            _sessionStore = sessionStore;
            _challenge = challenge;
            _uiDispatcher = uiDispatcher;

            _sessionState.Changed += OnSessionChanged;
            ApplySession(_sessionState.Current);
        }

        private DelegateCommand? _openLoginCommand;
        /// <summary>未登录时按钮点击 → 弹 QR 扫码 Overlay。</summary>
        public DelegateCommand OpenLoginCommand =>
            _openLoginCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    await _challenge.ShowAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"{nameof(UserProfileViewModel)}.{nameof(OpenLoginCommand)}");
                }
            });

        private DelegateCommand? _togglePopupCommand;
        /// <summary>已登录时按钮点击 → 开/关 Popup。</summary>
        public DelegateCommand TogglePopupCommand =>
            _togglePopupCommand ??= new DelegateCommand(() => IsPopupOpen = !IsPopupOpen);

        private DelegateCommand? _logoutCommand;
        /// <summary>
        /// Popup 内"退出登录"点击：清 <c>auth.dat</c> + 清内存 token → 关 Popup → 弹 QR 扫码。
        /// 顺序不能反：先关 Popup 再弹 Overlay，避免 Popup 遮挡 AuthChallenge Overlay。
        /// </summary>
        public DelegateCommand LogoutCommand =>
            _logoutCommand ??= new DelegateCommand(async () =>
            {
                try
                {
                    _sessionStore.Clear();
                    _sessionState.Set(null);
                    IsPopupOpen = false;
                    await _challenge.ShowAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"{nameof(UserProfileViewModel)}.{nameof(LogoutCommand)}");
                }
            });

        private void OnSessionChanged(object? sender, HeyboxLoginState? state)
        {
            _uiDispatcher.InvokeAsync(() => ApplySession(state));
        }

        private void ApplySession(HeyboxLoginState? state)
        {
            if (state is null || string.IsNullOrEmpty(state.Session.Pkey))
            {
                IsLoggedIn = false;
                Nickname = string.Empty;
                AvatarUrl = DefaultAvatarPackUri;
                IsPopupOpen = false;
                return;
            }

            IsLoggedIn = true;
            // 昵称/头像要另查一次才有，拿不到时留空 → 退回默认头像，不影响"已登录"状态。
            Nickname = state.Nickname;
            AvatarUrl = string.IsNullOrEmpty(state.Avatar) ? DefaultAvatarPackUri : state.Avatar;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sessionState.Changed -= OnSessionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
