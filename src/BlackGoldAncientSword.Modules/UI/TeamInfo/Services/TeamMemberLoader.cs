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

                // unified/player 已归一化三源，不再分派。
                var profileResp = await NarakaApiClient.GetPlayerProfileAsync(
                    ctx.Source.ToApiString(), ctx.RoleIdSimple, ct).ConfigureAwait(false);
                var userInfo = UnifiedMapper.MapPlayer(profileResp);

                if (userInfo == null)
                {
                    result.Failed = true;
                    result.FailMsg = profileResp?.Msg;
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
        /// 先用本地 UID 搜索（若提供且命中直接返回），否则用用户名搜索。
        /// UID 分支的 <see cref="NarakaApiException"/> 被吞掉走回退——本地用户查询绝不能因 UID
        /// 路径异常整格失败，用户名兜底至少与旧行为等价。返回 (搜索结果, 用户名分支的 msg)。
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
                    if (uidSearch != null) return (uidSearch, uidResp?.Msg);
                }
                catch (OperationCanceledException) { throw; }
                catch (NarakaApiException) { /* UID 路径失败 → 回退用户名 */ }
            }

            var resp = await SearchPreferDaShenAsync(userName, ct).ConfigureAwait(false);
            return (UnifiedMapper.MapSearch(resp), resp?.Msg);
        }

        /// <summary>
        /// 优先用网易大神数据源搜索：先传 source=dashen；大神查无（data 空）时回退不带 source
        /// 由后端按名称/ID 选默认源，保证既满足"尽量查大神"又不因大神无该玩家而整体查不到。
        /// </summary>
        private static async Task<Framework.Http.Generated.SearchRecordResponse?> SearchPreferDaShenAsync(
            string keyword, CancellationToken ct)
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
        /// <summary>stats 子查询；可能为 null（API 未返回有效数据）。</summary>
        public PlayerStatsLoadResult? Stats { get; set; }
    }
}
