using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Modularity;
using Prism.Regions;

namespace BlackGoldAncientSword.App.Services
{
    /// <summary>
    /// 并发单飞的登录弹窗调度：任意个后台请求 401 只弹一次 Overlay，
    /// AuthChallengePage 拿到 token 后调 <see cref="Complete"/> 让所有 await 者一并 resume。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class AuthChallengeService : IAuthChallengeService
    {
        private readonly IRegionManager _regionManager;
        private readonly IModuleManager _moduleManager;

        private readonly object _sync = new();
        private TaskCompletionSource<bool>? _current;

        public AuthChallengeService(IRegionManager regionManager, IModuleManager moduleManager)
        {
            _regionManager = regionManager;
            _moduleManager = moduleManager;
        }

        public Task<bool> ShowAsync(CancellationToken ct = default)
        {
            TaskCompletionSource<bool> tcs;
            bool shouldNavigate;
            lock (_sync)
            {
                if (_current is null)
                {
                    _current = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shouldNavigate = true;
                }
                else
                {
                    shouldNavigate = false;
                }
                tcs = _current;
            }

            if (ct.CanBeCanceled)
                ct.Register(() => TryFail(new OperationCanceledException(ct)));

            if (shouldNavigate)
                NavigateToOverlay();

            return tcs.Task;
        }

        public void Complete(bool success)
        {
            TaskCompletionSource<bool>? tcs;
            lock (_sync)
            {
                tcs = _current;
                _current = null;
            }
            tcs?.TrySetResult(success);
        }

        private void TryFail(Exception ex)
        {
            TaskCompletionSource<bool>? tcs;
            lock (_sync)
            {
                tcs = _current;
                _current = null;
            }
            tcs?.TrySetException(ex);
        }

        private void NavigateToOverlay()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    try { _moduleManager.LoadModule("AuthChallengeModule"); } catch { /* already loaded */ }
                    _regionManager.RequestNavigate(GlobalConstant.AuthChallengeRegion, PageNames.AuthChallengePage);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(AuthChallengeService)}.{nameof(NavigateToOverlay)}] {ex}");
                    TryFail(ex);
                }
            }), DispatcherPriority.Normal);
        }
    }
}
