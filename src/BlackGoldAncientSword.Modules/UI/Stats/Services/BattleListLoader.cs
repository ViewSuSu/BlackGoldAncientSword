using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{
    /// <summary>
    /// Stats 页专用：拉取近期对局列表，并为列表项串行拉取 HonorTitles。
    /// 业务从 StatsPageViewModel 剥离，VM 仅负责将结果映射到 UI 绑定属性。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class BattleListLoader
    {
        private readonly PlayerStatsLoader _playerStatsLoader;

        public BattleListLoader(PlayerStatsLoader playerStatsLoader)
        {
            _playerStatsLoader = playerStatsLoader;
        }

        /// <summary>拉取玩家最近对局列表（API 默认最多 10 条）。</summary>
        public async Task<GetRecentBattlesResponse?> FetchBattleListAsync(string roleId, CancellationToken ct)
        {
            try
            {
                return await NarakaApiClient.GetRecentBattlesAsync(roleId, ct: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(BattleListLoader)}] FetchBattleListAsync failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 串行为对局列表中的每一项拉取 HonorTitles，每拉完一项调用 <paramref name="onItemReady"/>。
        /// 串行而非并行：服务端对单玩家高频接口有节流，并行经验上会触发 429。
        /// </summary>
        /// <param name="roleId">玩家 roleIdSimple。</param>
        /// <param name="battleItems">对局列表（顺序即 UI 显示顺序）。</param>
        /// <param name="onItemReady">
        /// 每条对局加载完成后的回调：(index, honorTitles)。
        /// honorTitles 为空数组表示该对局无荣誉称号或拉取失败。
        /// </param>
        /// <param name="ct">取消令牌；取消时方法立即返回，已发出的回调结果不撤销。</param>
        public async Task FetchHonorTitlesForListAsync(
            string roleId,
            List<RecentBattleItem> battleItems,
            Action<int, HonorTitleInfo[]> onItemReady,
            CancellationToken ct)
        {
            for (int i = 0; i < battleItems.Count; i++)
            {
                if (ct.IsCancellationRequested) return;

                var battleId = battleItems[i].BattleId?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(battleId))
                {
                    onItemReady(i, Array.Empty<HonorTitleInfo>());
                    continue;
                }

                var detail = await _playerStatsLoader.FetchHonorTitlesAsync(roleId, battleId, ct).ConfigureAwait(false);
                var titles = detail?.Data?.HonorTitles?.ToArray() ?? Array.Empty<HonorTitleInfo>();
                onItemReady(i, titles);
            }
        }
    }
}
