using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Modularity;
using Prism.Regions;

namespace BlackGoldAncientSword.App.Services
{

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

        /// <summary>
        /// 等更新弹窗先处理完的最长时间。超过就照常弹登录——宁可两层浮层叠一下，
        /// 也不能让"点登录"变成永远没反应。
        /// </summary>
        private const int UpdateGateGraceMs = 1500;

        public async Task<bool> ShowAsync(CancellationToken ct = default)
        {
            // 等更新 gate 只为避免登录浮层压在更新卡片上。但绝不能无限等：
            // 只要更新卡片没走 DismissOverlay 释放 gate（× / Esc 关闭、导航失败没显示出来、
            // Updater 半途退出的事件漏掉），gate 就永远不 Complete，这里会永久挂起，
            // 表现为用户点侧栏"登录"毫无反应。故给一个短超时兜底，超时照常弹登录。
            // 启动路径下 gate 已在 App.OnStartup [4] 里 await 过且 finally 兜底 Complete，
            // WaitAsync 直接返回已完成的 Task，这里零延迟。
            try
            {
                var gate = _updateGate.WaitAsync(ct);
                if (!gate.IsCompleted)
                    await Task.WhenAny(gate, Task.Delay(UpdateGateGraceMs, ct)).ConfigureAwait(false);
            }
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
                    try { _moduleManager.LoadModule("AuthChallengeModule"); } catch {  }
                    _regionManager.RequestNavigate(GlobalConstant.AuthChallengeRegion, PageNames.AuthChallengePage);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"{nameof(AuthChallengeService)}.{nameof(NavigateToOverlay)}");
                    TryFail(ex);
                }
            }), DispatcherPriority.Normal);
        }
    }
}
