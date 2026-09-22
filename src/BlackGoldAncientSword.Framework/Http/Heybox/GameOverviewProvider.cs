using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class GameOverviewProvider
    {
        private const string GameAppId = "1203220";

        private readonly object _sync = new();
        private Task<UnifiedGameOverview?>? _inFlight;

        public Task<UnifiedGameOverview?> GetAsync(CancellationToken ct)
        {
            Task<UnifiedGameOverview?> task;

            lock (_sync)
            {
                task = _inFlight ??= LoadAsync();
            }

            return task.WaitAsync(ct);
        }

        private async Task<UnifiedGameOverview?> LoadAsync()
        {
            UnifiedGameOverview? overview = null;

            try
            {
                await WaitForPipelineAsync().ConfigureAwait(false);

                var response = await NarakaApiClient
                    .GetGameDetailAsync(steamAppid: GameAppId, ct: CancellationToken.None)
                    .ConfigureAwait(false);
                overview = UnifiedGameOverviewMapper.Map(response);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(GameOverviewProvider));
            }

            lock (_sync)
            {
                _inFlight = null;
            }

            return overview;
        }

        private static async Task WaitForPipelineAsync()
        {
            for (var i = 0; i < 40 && !AppStartupState.IsPipelineReady; i++)
                await Task.Delay(250).ConfigureAwait(false);
        }
    }
}
