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
        private readonly IUpdateGateService _updateGate;

        private readonly object _sync = new();
        private TaskCompletionSource<bool>? _current;

        public AuthChallengeService(
            IRegionManager regionManager,
            IModuleManager moduleManager,
            IUpdateGateService updateGate)
        {
            _regionManager = regionManager;
            _moduleManager = moduleManager;
            _updateGate = updateGate;
        }

        public async Task<bool> ShowAsync(CancellationToken ct = default)
        {
            // 启动期约束：若正巧检测到新版本，"发现新版本"弹窗必须先让用户处理完，
            // 登录 challenge 才能弹。App.OnStartup 保证无新版 / 异常 / 用户 Dismiss 三条路径
            // 都会调 updateGate.Complete()，所以正常路径不会永挂。
            try { await _updateGate.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }

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

            return await tcs.Task.ConfigureAwait(false);
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
