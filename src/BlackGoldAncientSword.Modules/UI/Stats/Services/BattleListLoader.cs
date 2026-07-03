using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
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
                System.Diagnostics.Debug.WriteLine($"[{nameof(BattleListLoader)}] FetchBattleListAsync failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 串行为对局列表中的每一项拉取 HonorTitles（miniProgram），或从 heyBox 详情的 tags 抽取。
        /// 串行而非并行：服务端对单玩家高频接口有节流，并行经验上会触发 429。
        /// 每拉完一项调用 <paramref name="onItemReady"/>。
        /// </summary>
        public async Task FetchHonorTitlesForListAsync(
            PlayerSourceContext ctx,
            List<UnifiedRecentBattleItem> battleItems,
            Action<int, IReadOnlyList<UnifiedHonorTitle>> onItemReady,
            CancellationToken ct)
        {
            for (int i = 0; i < battleItems.Count; i++)
            {
                if (ct.IsCancellationRequested) return;

                var battleId = battleItems[i].BattleId;
                if (string.IsNullOrEmpty(battleId))
                {
                    onItemReady(i, Array.Empty<UnifiedHonorTitle>());
                    continue;
                }

                var detail = await _playerStatsLoader.FetchBattleDetailAsync(ctx, battleId, ct).ConfigureAwait(false);
                var titles = detail?.Personal?.HonorTitles ?? Array.Empty<UnifiedHonorTitle>();
                onItemReady(i, titles);
            }
        }
    }
}
