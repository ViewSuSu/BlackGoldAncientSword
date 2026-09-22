using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxSessionRestorer : IHeyboxSessionRestorer
    {
        private readonly IHeyboxSessionState _state;
        private readonly HeyboxHomeDataProvider _homeData;
        private readonly IHeyboxSessionProbe _probe;

        public HeyboxSessionRestorer(
            IHeyboxSessionState state,
            HeyboxHomeDataProvider homeData,
            IHeyboxSessionProbe probe)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _homeData = homeData ?? throw new ArgumentNullException(nameof(homeData));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        }

        public async Task<bool> TryRestoreAsync(
            HeyboxSession? session,
            string roleId,
            string server,
            CancellationToken ct = default)
        {
            if (session is null)
                return false;

            _state.Set(HeyboxLoginState.FromSession(session));

            HeyboxSessionProbeOutcome outcome;
            try
            {
                outcome = await _probe
                    .ProbeAsync(roleId ?? string.Empty, server, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                return true;
            }

            if (!outcome.Alive)
            {
                _state.Set(null);
                return false;
            }

            if (outcome.Home is not null)
                _homeData.Seed(roleId ?? string.Empty, server, season: null, battleTid: null, outcome.Home);

            return true;
        }
    }
}
