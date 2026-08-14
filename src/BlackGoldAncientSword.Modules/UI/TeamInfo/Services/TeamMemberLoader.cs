using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// 拉取队伍内单个玩家的完整资料：先 SearchRecord 拿 roleId + dataSource，
    /// 再按数据源分派 mini-program/heybox user 接口，最后通过 <see cref="PlayerStatsLoader"/> 拿 stats。
    /// 结果聚合为 <see cref="MemberLoadResult"/>。全程无 UI 线程依赖，由 VM 自行把 DTO 字段填回 TeamMemberInfo。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class TeamMemberLoader
    {
        private readonly PlayerStatsLoader _statsLoader;

        public TeamMemberLoader(PlayerStatsLoader statsLoader)
        {
            _statsLoader = statsLoader;
        }

        /// <summary>
        /// 拉取队员资料：失败/未查到时返回 <see cref="MemberLoadResult.Failed"/>=true 而非抛异常；
        /// 后端业务错误（<see cref="NarakaApiException"/>）在此层 catch 住，把 msg 透传到
        /// <see cref="MemberLoadResult.FailMsg"/>，由 VM 决定卡片如何展示（不吞成"查询失败"）。
        /// 取消通过 ct 抛 <see cref="System.OperationCanceledException"/>，由调用方捕获。
        /// </summary>
        public async Task<MemberLoadResult> LoadAsync(
            string userName,
            double? selectedSeasonCode,
            GameModeCategory category,
            TeamSize teamSize,
            CancellationToken ct,
            string? localUidOverride = null)
        {
            var result = new MemberLoadResult();

            try
            {
                // 本地用户格：优先用本地 UID（Player.log aid / 活跃账号 section-key）查询。
                // SearchRecord "支持昵称或角色ID"，UID 一定命中；用户名可能重名/查无。
                // UID 查不到再回退用户名，保证渐进增强、不比纯用户名路径更差。
                var (search, searchMsg) = await SearchByUidThenNameAsync(userName, localUidOverride, ct);
                if (search == null)
                {
                    // code=200 但 data 空（如"查无此人"）：透传 msg 给 UI。
                    result.Failed = true;
                    result.FailMsg = searchMsg;
                    return result;
                }

                var ctx = new PlayerSourceContext(search.RoleIdSimple, search.DataSource);
                result.SourceContext = ctx;

                // 名字/头像统一优先 dashen 源查 profile：dashen 对隐藏昵称的玩家返回真实昵称，
                // 而 heyBox 源会对这类玩家返回占位"匿名玩家"；search 返回的 source 不稳定
                // （dashen 查无时后端自动降级到 heyBox/miniProgram），若按它分派会出现卡片名字
                // 显示"匿名玩家"、点进战绩页却显示真名的不一致。roleId 三源通用，优先 dashen 拿真名。
                // dashen 不可用时（上游异常）回退 search 返回的 source，避免整卡失败。
                var (userInfo, profileMsg) = await FetchProfileAsync(ctx, ct);

                if (userInfo == null)
                {
                    result.Failed = true;
                    result.FailMsg = profileMsg;
                    return result;
                }

                result.UserName = string.IsNullOrEmpty(userInfo.RoleName) ? userName : userInfo.RoleName;
                result.Level = "Lv." + userInfo.RoleLevel.ToString();
                result.UID = userInfo.Uid;
                result.AvatarUrl = userInfo.HeadIcon;
                // unified/player 不返回各模式段位分（旧 miniProgram 的 surviveXxxGrade 已无对应字段）；
                // 当前选中模式的段位改由下方 stats 子查询（GetSeasonSummary）提供，这三项置 0（UI 未绑定）。
                result.SoloRankScore = 0;
                result.DuoRankScore = 0;
                result.TrioRankScore = 0;

                var seasonId = selectedSeasonCode;
                var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
                result.Stats = await _statsLoader.LoadAsync(ctx, seasonId, gameMode, ct).ConfigureAwait(false);
                return result;
            }
            catch (NarakaApiException ex)
            {
                // 任一阶段的业务/HTTP 错误：msg 原文回填给 VM，卡片中心直接展示。
                result.Failed = true;
                result.FailMsg = ex.Msg;
                return result;
            }
        }

        /// <summary>
        /// 复用已解析的 <see cref="PlayerSourceContext"/> 只重查 season（stats）。
        /// 筛选器（赛季/排数/大类）变更时调用：这些条件只影响 stats，搜索 identity（roleId/数据源）
        /// 与玩家资料（名字/头像/等级）不变，无需重复 search/player。仅当首次加载已成功拿到 ctx 后使用。
        /// </summary>
        public async Task<PlayerStatsLoadResult?> LoadStatsOnlyAsync(
            PlayerSourceContext ctx,
            double? selectedSeasonCode,
            GameModeCategory category,
            TeamSize teamSize,
            CancellationToken ct)
        {
            var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
            return await _statsLoader.LoadAsync(ctx, selectedSeasonCode, gameMode, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 查玩家资料（名字/头像/等级/UID）。直接用 search 返回的 source 查询：
        /// search 不传 source 时后端默认优先 dashen，查无自动降级并在 source 字段回传实际源；
        /// player 接口若无脑传 dashen，可能因降级源不同导致查无或占位昵称。返回 (资料, 失败 msg)。
        /// </summary>
        private static async Task<(UnifiedUserInfo? userInfo, string? msg)> FetchProfileAsync(
            PlayerSourceContext ctx, CancellationToken ct)
        {
            var resp = await NarakaApiClient.GetPlayerProfileAsync(
                ctx.Source.ToApiString(), ctx.RoleIdSimple, ct).ConfigureAwait(false);
            var info = UnifiedMapper.MapPlayer(resp);
            return (info, resp?.Msg);
        }

        /// <summary>
        /// 查询角色：有 UID（<paramref name="localUidOverride"/>）时只用 UID 查——UID 唯一可查、不重名，
        /// 能精确命中，绝不再回退到用户名重查一遍。队友卡 userName 与 UID 是同一带前缀 ID，本地卡
        /// userName 是昵称，统一原则都是"有 UID 只用 UID"，避免对同一目标重复发 HTTP。
        /// 仅当没有 UID 时才用用户名查。返回 (搜索结果, msg)。
        /// </summary>
        private static async Task<(UnifiedSearchResult? search, string? msg)> SearchByUidThenNameAsync(
            string userName, string? localUidOverride, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(localUidOverride))
            {
                try
                {
                    var uidResp = await SearchPreferDaShenAsync(localUidOverride, ct).ConfigureAwait(false);
                    var uidSearch = UnifiedMapper.MapSearch(uidResp);
                    return (uidSearch, uidResp?.Msg);
                }
                catch (OperationCanceledException) { throw; }
                catch (NarakaApiException)
                {
                    // 后端业务/HTTP 错误（如 429/500）：不重试用户名，按查无交由上层展示失败。
                    return (null, null);
                }
            }

            var resp = await SearchPreferDaShenAsync(userName, ct).ConfigureAwait(false);
            return (UnifiedMapper.MapSearch(resp), resp?.Msg);
        }

        /// <summary>
        /// 搜索角色：不传 source，后端默认优先网易大神（dashen）。大神查无时后端自动降级到其它源，
        /// 并在响应体 source 字段回传实际源；后续按返回的 source 分派即可。
        /// </summary>
        private static Task<Framework.Http.Generated.SearchRecordResponse?> SearchPreferDaShenAsync(
            string keyword, CancellationToken ct)
        {
            return NarakaApiClient.SearchRecordAsync(keyword, null, ct);
        }
    }

    /// <summary>
    /// TeamMemberLoader 的返回 DTO。VM 把字段映射到 TeamMemberInfo 上。
    /// </summary>
    public class MemberLoadResult
    {
        public bool Failed { get; set; }
        /// <summary>
        /// 失败时后端 msg 原文；网络异常/后端未返回 msg 时为 null。
        /// VM 在 msg 为空时回退到本地化的"查询失败"。
        /// </summary>
        public string? FailMsg { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string UID { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public double SoloRankScore { get; set; }
        public double DuoRankScore { get; set; }
        public double TrioRankScore { get; set; }
        /// <summary>search+player 成功后锁定的玩家查询上下文（roleIdSimple + 数据源）。
        /// 供后续筛选器变更时只重查 season 复用，避免对同一玩家重复 search/player。</summary>
        public PlayerSourceContext? SourceContext { get; set; }
        /// <summary>stats 子查询；可能为 null（API 未返回有效数据）。</summary>
        public PlayerStatsLoadResult? Stats { get; set; }
    }
}
