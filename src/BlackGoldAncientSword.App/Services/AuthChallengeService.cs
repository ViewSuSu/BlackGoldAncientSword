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

        public async Task<bool> ShowAsync(CancellationToken ct = default)
        {
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
