using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{
    /// <summary>
    /// Stats 页专用：从 NarakaApiClient 拉取"单个玩家"相关数据并归一化为 Unified 域模型。
    /// SearchRecord 返回的 dataSource 决定后续所有接口走 miniProgram 还是 heyBox 路径。
    /// 与 <see cref="BlackGoldAncientSword.Modules.UI.TeamInfo.Services"/> 下同名类完全隔离（命名空间不同）。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class PlayerStatsLoader
    {
        /// <summary>根据角色昵称解析 roleIdSimple + dataSource。</summary>
        public async Task<UnifiedSearchResult?> SearchRoleByNameAsync(string playerName, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.SearchRecordAsync(playerName, ct).ConfigureAwait(false);
                return UnifiedMapper.MapSearch(resp);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(PlayerStatsLoader)}] SearchRoleByNameAsync failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>查询玩家基础信息（昵称、头像、等级、当前赛季）。</summary>
        public Task<UnifiedUserInfo?> FetchUserInfoAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            return ctx.Source == DataSource.HeyBox
                ? InvokeAsync(async () => UnifiedMapper.MapHeyBoxUser(
                    await NarakaApiClient.HeyBoxUserInfoAsync(ctx.RoleIdSimple, ct).ConfigureAwait(false),
                    ctx.RoleIdSimple))
                : InvokeAsync(async () => UnifiedMapper.MapMiniProgramUser(
                    await NarakaApiClient.GetUserInfoAsync(ctx.RoleIdSimple, ct).ConfigureAwait(false)));
        }

        /// <summary>查询赛季列表（两套共享 endpoint）。</summary>
        public async Task<System.Collections.Generic.List<UnifiedSeason>?> FetchSeasonsAsync(CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.QuerySeasonsAsync(ct).ConfigureAwait(false);
                return UnifiedMapper.MapSeasons(resp);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(PlayerStatsLoader)}] FetchSeasonsAsync failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 查询指定赛季、指定模式的战绩明细。
        /// heyBox 分支不吃 seasonId/gameMode，返回 HeyBoxUserInfo 的 overview[]（等价于当前赛季的综合统计）。
        /// </summary>
        public Task<UnifiedPlayerStats?> FetchPlayerStatsAsync(
            PlayerSourceContext ctx, double? seasonId, GameMode gameMode, CancellationToken ct)
        {
            return ctx.Source == DataSource.HeyBox
                ? InvokeAsync(async () => UnifiedMapper.MapHeyBoxStats(
                    await NarakaApiClient.HeyBoxUserInfoAsync(ctx.RoleIdSimple, ct).ConfigureAwait(false)))
                : InvokeAsync(async () => UnifiedMapper.MapMiniProgramStats(
                    await NarakaApiClient.GetPlayerStatsAsync(ctx.RoleIdSimple, seasonId, gameMode, ct).ConfigureAwait(false)));
        }

        /// <summary>
        /// 查询单场对局详情（个人 + 可空 team + 可空 top5）。
        /// heyBox 分支只有个人数据，team/top5 恒为 null。
        /// </summary>
        public Task<UnifiedBattleDetail?> FetchBattleDetailAsync(PlayerSourceContext ctx, string battleId, CancellationToken ct)
        {
            return ctx.Source == DataSource.HeyBox
                ? InvokeAsync(async () => UnifiedMapper.MapHeyBoxBattleDetail(
                    await NarakaApiClient.HeyBoxBattleDetailAsync(battleId, ct).ConfigureAwait(false)))
                : FetchMiniProgramBattleDetailAsync(ctx, battleId, ct);
        }

        /// <summary>miniProgram 分支：并行拉 personal / team / top5 三接口后合并为 UnifiedBattleDetail。</summary>
        private static async Task<UnifiedBattleDetail?> FetchMiniProgramBattleDetailAsync(
            PlayerSourceContext ctx, string battleId, CancellationToken ct)
        {
            try
            {
                var personalTask = NarakaApiClient.GetBattleDetailAsync(ctx.RoleIdSimple, battleId, ct);
                var teamTask = InvokeApiSafeAsync(() => NarakaApiClient.GetTeamBattleDetailAsync(ctx.RoleIdSimple, battleId, ct));
                var top5Task = InvokeApiSafeAsync(() => NarakaApiClient.GetTop5BattleDetailAsync(ctx.RoleIdSimple, battleId, ct));
                await Task.WhenAll(personalTask, teamTask, top5Task).ConfigureAwait(false);
                return UnifiedMapper.MapMiniProgramBattleDetail(personalTask.Result, teamTask.Result, top5Task.Result);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(PlayerStatsLoader)}] FetchMiniProgramBattleDetailAsync failed: {ex.Message}");
                return null;
            }
        }

        private static async Task<T?> InvokeApiSafeAsync<T>(Func<Task<T>> call) where T : class
        {
            try { return await call().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        /// <summary>
        /// 统一包装：把 API 抛出的"未知底层异常"（网络异常/反序列化异常等，无 msg 可展示）吞掉并返回 null，
        /// 这样 VM 端只需做 null 判断，不必散落 try/catch。
        /// <see cref="OperationCanceledException"/> 与 <see cref="NarakaApiException"/> 仍向上抛出——
        /// 前者保证 ct 语义，后者携带响应体 msg，由 VM 统一展示。
        /// </summary>
        private static async Task<T?> InvokeAsync<T>(Func<Task<T?>> call) where T : class
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NarakaApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{nameof(PlayerStatsLoader)}] API call failed: {ex.Message}");
                return null;
            }
        }
    }
}
