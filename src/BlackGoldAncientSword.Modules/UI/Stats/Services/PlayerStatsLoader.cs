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
    /// source 仅作为 query 参数随请求下发。search 默认优先网易大神（dashen）。
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
        /// 优先网易大神搜索：先 source=dashen，查无（data 空）时回退不带 source 让后端选默认源。
        /// 满足"尽量查大神"，同时不因大神无该玩家而整体查不到。
        /// </summary>
        private static async Task<SearchRecordResponse?> SearchPreferDaShenAsync(string keyword, CancellationToken ct)
        {
            var daShen = DataSource.DaShen.ToApiString();
            try
            {
                var resp = await NarakaApiClient.SearchRecordAsync(keyword, daShen, ct).ConfigureAwait(false);
                if (UnifiedMapper.MapSearch(resp) != null) return resp;
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { /* 大神源查无/被拒 → 回退默认源 */ }

            return await NarakaApiClient.SearchRecordAsync(keyword, null, ct).ConfigureAwait(false);
        }

        /// <summary>查询玩家基础信息（昵称、头像、等级）。</summary>
        public Task<UnifiedUserInfo?> FetchUserInfoAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            return InvokeAsync(async () => UnifiedMapper.MapPlayer(
                await NarakaApiClient.GetPlayerProfileAsync(ctx.Source.ToApiString(), ctx.RoleIdSimple, ct).ConfigureAwait(false)));
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
