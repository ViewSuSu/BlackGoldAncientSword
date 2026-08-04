using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 后台定时器：<see cref="IAuthTokenState.Current"/> 存在但即将过期时主动 refresh；
    /// refresh 失败则清 token + 弹登录 Overlay。
    /// <para>
    /// 与 <see cref="AuthTokenHandler"/> 里的响应式 401 兜底互补：即使用户长时间不发 API 请求，
    /// token 到点也会被主动感知，避免下一次交互卡顿等待 refresh。
    /// </para>
    /// <para>
    /// 生命周期由 App 层持有（Singleton）。<see cref="Start"/> 幂等，<see cref="Dispose"/> 停 timer。
    /// </para>
    /// </summary>
    public sealed class AuthTokenExpiryMonitor : IDisposable
    {
        private readonly IAuthTokenState _state;
        private readonly IAuthTokenStore _store;
        private readonly IAuthTokenRefresher _refresher;
        private readonly IAuthChallengeService _challenge;
        private readonly Func<long> _nowMs;
        private readonly long _refreshLeadTimeMs;
        private readonly TimeSpan _checkInterval;

        private Timer? _timer;
        private int _running;
        private int _started;

        public AuthTokenExpiryMonitor(
            IAuthTokenState state,
            IAuthTokenStore store,
            IAuthTokenRefresher refresher,
            IAuthChallengeService challenge)
            : this(state, store, refresher, challenge,
                nowMs: () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                refreshLeadTimeMs: 30_000,
                checkInterval: TimeSpan.FromSeconds(20))
        { }

        internal AuthTokenExpiryMonitor(
            IAuthTokenState state,
            IAuthTokenStore store,
            IAuthTokenRefresher refresher,
            IAuthChallengeService challenge,
            Func<long> nowMs,
            long refreshLeadTimeMs,
            TimeSpan checkInterval)
        {
            _state = state;
            _store = store;
            _refresher = refresher;
            _challenge = challenge;
            _nowMs = nowMs;
            _refreshLeadTimeMs = refreshLeadTimeMs;
            _checkInterval = checkInterval;
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            _timer = new Timer(OnTick, null, _checkInterval, _checkInterval);
        }

        internal Task TickAsync(CancellationToken ct = default) => RunCheckAsync(ct);

        private void OnTick(object? _)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            _ = RunCheckAsync(CancellationToken.None)
                .ContinueWith(_ => Interlocked.Exchange(ref _running, 0), TaskScheduler.Default);
        }

        private async Task RunCheckAsync(CancellationToken ct)
        {
            try
            {
                var token = _state.Current;
                if (token is null) return;
                if (token.ExpiresAtUnixMs <= 0) return; // 无 exp 信息不主动动作，交由 401 兜底

                var msUntilExpiry = token.ExpiresAtUnixMs - _nowMs();
                if (msUntilExpiry > _refreshLeadTimeMs) return;

                if (!string.IsNullOrEmpty(token.RefreshToken))
                {
                    var next = await _refresher.RefreshAsync(token.RefreshToken, ct).ConfigureAwait(false);
                    if (next != null)
                    {
                        // /refresh-token 响应不含 profile；合并当前 UserJson，避免每 20s 静默刷新丢头像/昵称。
                        if (string.IsNullOrEmpty(next.UserJson) && !string.IsNullOrEmpty(token.UserJson))
                            next = next with { UserJson = token.UserJson };
                        _state.Set(next);
                        _store.Save(next);
                        return;
                    }
                }

                // 无 refresh token / refresh 失败 → 清本地凭证并弹登录
                _state.Set(null);
                _store.Clear();
                await _challenge.ShowAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(AuthTokenExpiryMonitor), "tick failed");
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
