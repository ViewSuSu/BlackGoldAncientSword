using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{
    /// <summary>
    /// Stats 页专用：从 NarakaApiClient 拉取"单个玩家"相关数据
    /// （用户信息、赛季列表、对局战绩、对局详情）。
    /// 与 <see cref="BlackGoldAncientSword.Modules.UI.TeamInfo.Services"/> 下同名类完全隔离（命名空间不同）。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class PlayerStatsLoader
    {
        /// <summary>根据角色昵称解析 roleIdSimple。</summary>
        public Task<SearchRecordResponse?> SearchRoleByNameAsync(string playerName, CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.SearchRecordAsync(playerName, ct));
        }

        /// <summary>查询玩家基础信息（昵称、头像、等级等）。</summary>
        public Task<GetUserInfoResponse?> FetchUserInfoAsync(string roleId, CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.GetUserInfoAsync(roleId, ct));
        }

        /// <summary>查询赛季列表。</summary>
        public Task<QuerySeasonsResponse?> FetchSeasonsAsync(CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.QuerySeasonsAsync(ct));
        }

        /// <summary>查询指定赛季、指定模式的战绩明细。</summary>
        public Task<GetPlayerStatsResponse?> FetchPlayerStatsAsync(
            string roleId, double? seasonId, GameMode gameMode, CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.GetPlayerStatsAsync(roleId, seasonId, gameMode, ct));
        }

        /// <summary>查询单场对局个人详情（含 HonorTitles）。</summary>
        public Task<GetBattleDetailResponse?> FetchHonorTitlesAsync(string roleId, string battleId, CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.GetBattleDetailAsync(roleId, battleId, ct));
        }

        /// <summary>查询单场对局个人详情（等同于 FetchHonorTitlesAsync，语义更贴近“对局详情”业务）。</summary>
        public Task<GetBattleDetailResponse?> FetchBattleDetailAsync(string roleId, string battleId, CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.GetBattleDetailAsync(roleId, battleId, ct));
        }

        /// <summary>查询单场对局的队伍详情（队友装备/伤害）。</summary>
        public Task<GetTeamBattleDetailResponse?> FetchTeamBattleDetailAsync(string roleId, string battleId, CancellationToken ct)
        {
            return InvokeAsync(() => NarakaApiClient.GetTeamBattleDetailAsync(roleId, battleId, ct));
        }

        /// <summary>
        /// 统一包装：把 API 抛出的非取消异常吞掉并返回 null，
        /// 这样 VM 端只需做 null 判断，不必散落 try/catch。
        /// <see cref="System.OperationCanceledException"/> 仍然向上抛出以保证 ct 语义。
        /// </summary>
        private static async Task<T?> InvokeAsync<T>(System.Func<Task<T>> call) where T : class
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(PlayerStatsLoader)}] API call failed: {ex.Message}");
                return null;
            }
        }
    }
}
