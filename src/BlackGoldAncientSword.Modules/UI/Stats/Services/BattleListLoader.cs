using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class BattleListLoader
    {

        private const int PageSize = 20;

        private readonly HeyboxRequestCache _cache;

        public BattleListLoader(HeyboxRequestCache cache)
        {
            _cache = cache;
        }

        public async Task<List<UnifiedRecentBattleItem>?> FetchBattleListAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            try
            {
                var resp = await _cache.RunAsync(
                    $"matchlist|{ctx.Server}|{ctx.RoleId}|{PageSize}|0",
                    () => NarakaApiClient.GetMatchListAsync(
                        server: ctx.Server, roleId: ctx.RoleId, limit: PageSize, offset: 0, ct: CancellationToken.None),
                    ct).ConfigureAwait(false);
                return UnifiedMapper.MapRecentMatches(resp?.Result?.MatchList);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(BattleListLoader)}.{nameof(FetchBattleListAsync)}");
                return null;
            }
        }
    }
}
