using System;
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
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(SearchRoleByNameAsync)}");
                return null;
            }
        }

        /// <summary>
        /// 本地用户查询：先用本地 UID（player_prefs 的 player_id，SearchRecord "支持昵称或角色ID"）搜，
        /// 命中直接返回；UID 查不到（未登录鉴权/接口不认该格式/查无）再回退用户名搜。
        /// <para>
        /// UID 一定唯一、不重名，而用户名可能重名或查无——本地用户查自己用 UID 更可靠。
        /// UID 分支的 <see cref="NarakaApiException"/> 被吞掉走回退，保证不比纯用户名路径更差。
        /// </para>
        /// </summary>
        public async Task<UnifiedSearchResult?> SearchRoleByUidThenNameAsync(
            string? localUid, string playerName, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(localUid))
            {
                try
                {
                    var uidResp = await NarakaApiClient.SearchRecordAsync(localUid, ct).ConfigureAwait(false);
                    var uidSearch = UnifiedMapper.MapSearch(uidResp);
                    if (uidSearch != null && !string.IsNullOrEmpty(uidSearch.RoleIdSimple))
                        return uidSearch;
                }
                catch (OperationCanceledException) { throw; }
                catch (NarakaApiException) { /* UID 路径失败 → 回退用户名 */ }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(SearchRoleByUidThenNameAsync)}", "uid search failed, fallback to name");
                }
            }

            return await SearchRoleByNameAsync(playerName, ct).ConfigureAwait(false);
        }

        /// <summary>查询玩家基础信息（昵称、头像、等级、当前赛季）。</summary>
        public Task<UnifiedUserInfo?> FetchUserInfoAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            return ctx.Source == DataSource.HeyBox
                ? InvokeAsync(async () => UnifiedMapper.MapHeyBoxUser(
                    await NarakaApiClient.HeyBoxUserInfoAsync(ctx.RoleIdSimple, ct: ct).ConfigureAwait(false),
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
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(FetchSeasonsAsync)}");
                return null;
            }
        }

        /// <summary>
        /// 查询指定赛季、指定模式的战绩明细。
        /// heyBox 分支同样吃 seasonId + battleTid：/heybox/user/info 会按赛季/模式返回对应的段位
        /// (playerInfo.level) 与统计 (overview[])，battleTid 由 GameMode 反查得到。
        /// </summary>
        public Task<UnifiedPlayerStats?> FetchPlayerStatsAsync(
            PlayerSourceContext ctx, double? seasonId, GameMode gameMode, CancellationToken ct)
        {
            return ctx.Source == DataSource.HeyBox
                ? InvokeAsync(async () => UnifiedMapper.MapHeyBoxStats(
                    await NarakaApiClient.HeyBoxUserInfoAsync(
                        ctx.RoleIdSimple, seasonId, gameMode.ToHeyBoxBattleTid(), ct).ConfigureAwait(false)))
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
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(FetchMiniProgramBattleDetailAsync)}");
                return null;
            }
        }

        private static async Task<T?> InvokeApiSafeAsync<T>(Func<Task<T>> call) where T : class
        {
            try { return await call().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 可选 API（team/top5）失败走软降级、不阻断主数据；但静默会让"部分数据缺失"无从排查，记 Warning。
                AppLog.Warning($"{nameof(PlayerStatsLoader)}.{nameof(InvokeApiSafeAsync)}", $"optional API failed, continuing without result: {ex.Message}");
                return null;
            }
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
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(InvokeAsync)}", "API call failed");
                return null;
            }
        }
    }
}
