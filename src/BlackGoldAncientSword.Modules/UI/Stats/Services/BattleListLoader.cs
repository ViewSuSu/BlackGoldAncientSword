using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{
    /// <summary>
    /// Stats 页专用：拉取近期对局列表（归一化到 <see cref="UnifiedRecentBattleItem"/>），
    /// 并为列表项串行拉取 HonorTitles。业务从 StatsPageViewModel 剥离，
    /// VM 仅负责将结果映射到 UI 绑定属性。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class BattleListLoader
    {
        private readonly PlayerStatsLoader _playerStatsLoader;

        public BattleListLoader(PlayerStatsLoader playerStatsLoader)
        {
            _playerStatsLoader = playerStatsLoader;
        }

        /// <summary>拉取玩家最近对局列表。miniProgram 默认最多 10 条，heyBox 支持 pageSize。</summary>
        public async Task<List<UnifiedRecentBattleItem>?> FetchBattleListAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            try
            {
                if (ctx.Source == DataSource.HeyBox)
                {
                    var resp = await NarakaApiClient.HeyBoxRecentBattlesAsync(
                        ctx.RoleIdSimple, pageIndex: 1, pageSize: 20, ct: ct).ConfigureAwait(false);
                    return UnifiedMapper.MapHeyBoxRecent(resp);
                }
                else
                {
                    var resp = await NarakaApiClient.GetRecentBattlesAsync(
                        ctx.RoleIdSimple, gameMode: null, ct: ct).ConfigureAwait(false);
                    return UnifiedMapper.MapMiniProgramRecent(resp);
                }
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
