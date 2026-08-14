using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{
    /// <summary>
    /// Stats 页专用：从 NarakaApiClient 拉取"单个玩家"相关数据并归一化为 Unified 域模型。
    /// 后端 unified 接口已归一化三源（miniProgram/heyBox/dashen），本类不再按数据源分派；
    /// search 不传 source，后端默认优先网易大神（dashen），查无时自动降级并回传实际 source。
    /// 与 <see cref="BlackGoldAncientSword.Modules.UI.TeamInfo.Services"/> 下同名类完全隔离（命名空间不同）。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class PlayerStatsLoader
    {
        /// <summary>根据角色昵称解析 roleIdSimple + dataSource（优先网易大神）。</summary>
        public async Task<UnifiedSearchResult?> SearchRoleByNameAsync(string playerName, CancellationToken ct)
        {
            try
            {
                var resp = await SearchPreferDaShenAsync(playerName, ct).ConfigureAwait(false);
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
        /// 本地用户查询：先用本地 UID（player_prefs 的 player_id）搜，命中直接返回；
        /// UID 查不到再回退用户名搜。两条路径都优先网易大神数据源。
        /// </summary>
        public async Task<UnifiedSearchResult?> SearchRoleByUidThenNameAsync(
            string? localUid, string playerName, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(localUid))
            {
                try
                {
                    var uidResp = await SearchPreferDaShenAsync(localUid, ct).ConfigureAwait(false);
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

        /// <summary>
        /// 搜索角色：不传 source，后端默认优先网易大神（dashen）。若大神查无，后端自动降级到
        /// 其它源并在响应体的 source 字段回传实际源；后续流程直接用返回的 source 分派即可。
        /// </summary>
        private static Task<SearchRecordResponse?> SearchPreferDaShenAsync(string keyword, CancellationToken ct)
        {
            return NarakaApiClient.SearchRecordAsync(keyword, null, ct);
        }

        /// <summary>
        /// 查询玩家基础信息（昵称、头像、等级）。直接用 search 返回的 source 查询：
        /// search 不传 source 时后端默认优先 dashen，查无自动降级并在 source 字段回传实际源；
        /// player 接口若无脑传 dashen，可能因降级源不同导致查无或占位昵称。与队友卡的资料查询
        /// （TeamInfo 的 <c>TeamMemberLoader.FetchProfileAsync</c>）保持一致，roleId 三源通用。
        /// </summary>
        public Task<UnifiedUserInfo?> FetchUserInfoAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            return FetchProfileAsync(ctx, ct);
        }

        /// <summary>
        /// 资料查询：按 search 返回的 source 走，与 <see cref="UnifiedMapper.MapPlayer"/> 配合，
        /// 返回统一玩家资料。source 异常时抛给上层统一处理。
        /// </summary>
        private static async Task<UnifiedUserInfo?> FetchProfileAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            var resp = await NarakaApiClient.GetPlayerProfileAsync(
                ctx.Source.ToApiString(), ctx.RoleIdSimple, ct).ConfigureAwait(false);
            return UnifiedMapper.MapPlayer(resp);
        }

        /// <summary>
        /// 赛季列表：unified 接口无独立 seasons endpoint，与网页 H5 一致取前端内嵌的
        /// <see cref="SeasonCatalog"/>（索引 0 为当前赛季，Code 为真实 seasonCode）。
        /// </summary>
        public Task<System.Collections.Generic.List<UnifiedSeason>?> FetchSeasonsAsync(CancellationToken ct)
        {
            return Task.FromResult<System.Collections.Generic.List<UnifiedSeason>?>(SeasonCatalog.All());
        }

        /// <summary>
        /// 查询指定赛季、指定模式的战绩明细（unified/season）。
        /// modeCode 口径为 battleTidHeyBox；seasonId 为 0/占位时传 null（后端用当前赛季）。
        /// </summary>
        public Task<UnifiedPlayerStats?> FetchPlayerStatsAsync(
            PlayerSourceContext ctx, double? seasonId, GameMode gameMode, CancellationToken ct)
        {
            var modeCode = gameMode.ToHeyBoxBattleTid().ToString(CultureInfo.InvariantCulture);
            var seasonCode = seasonId is null or 0
                ? null
                : seasonId.Value.ToString(CultureInfo.InvariantCulture);
            return InvokeAsync(async () => UnifiedMapper.MapSeasonSummary(
                await NarakaApiClient.GetSeasonSummaryAsync(
                    ctx.Source.ToApiString(), ctx.RoleIdSimple, modeCode, seasonCode, ct).ConfigureAwait(false)));
        }

        /// <summary>
        /// 查询单场对局详情（个人 + 可空 team + 可空 top5），并行拉三接口后合并。
        /// </summary>
        public Task<UnifiedBattleDetail?> FetchBattleDetailAsync(PlayerSourceContext ctx, string detailKey, CancellationToken ct)
        {
            return FetchMatchDetailAsync(ctx, detailKey, ct);
        }

        private static async Task<UnifiedBattleDetail?> FetchMatchDetailAsync(
            PlayerSourceContext ctx, string detailKey, CancellationToken ct)
        {
            try
            {
                var source = ctx.Source.ToApiString();
                var personalTask = NarakaApiClient.GetMatchDetailAsync(source, ctx.RoleIdSimple, detailKey, ct);
                var teamTask = InvokeApiSafeAsync(() => NarakaApiClient.GetMatchTeamAsync(source, ctx.RoleIdSimple, detailKey, ct));
                var top5Task = InvokeApiSafeAsync(() => NarakaApiClient.GetMatchTop5Async(source, ctx.RoleIdSimple, detailKey, ct));
                await Task.WhenAll(personalTask, teamTask, top5Task).ConfigureAwait(false);
                return UnifiedMapper.MapMatchDetail(personalTask.Result, teamTask.Result, top5Task.Result);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(FetchMatchDetailAsync)}");
                return null;
            }
        }

        private static async Task<T?> InvokeApiSafeAsync<T>(Func<Task<T>> call) where T : class
        {
            try { return await call().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Warning($"{nameof(PlayerStatsLoader)}.{nameof(InvokeApiSafeAsync)}", $"optional API failed, continuing without result: {ex.Message}");
                return null;
            }
        }

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
