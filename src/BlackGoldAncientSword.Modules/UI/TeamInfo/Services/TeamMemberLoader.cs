using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// 拉取队伍内单个玩家的完整资料：先 SearchRecord 拿 roleId，再 GetUserInfo 拿基础信息，
    /// 最后通过 <see cref="PlayerStatsLoader"/> 拿 stats。把结果聚合为 <see cref="MemberLoadResult"/>。
    /// 全程无 UI 线程依赖，由 VM 自行把 DTO 字段填回 TeamMemberInfo。
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
        /// 取消通过 ct 抛 <see cref="System.OperationCanceledException"/>，由调用方捕获。
        /// </summary>
        public async Task<MemberLoadResult> LoadAsync(
            string userName,
            double? selectedSeasonCode,
            GameModeCategory category,
            TeamSize teamSize,
            CancellationToken ct)
        {
            var result = new MemberLoadResult();

            var search = await NarakaApiClient.SearchRecordAsync(userName, ct).ConfigureAwait(false);
            if (search?.Data == null || string.IsNullOrEmpty(search.Data.RoleIdSimple))
            {
                result.Failed = true;
                return result;
            }

            var roleId = search.Data.RoleIdSimple;
            var userInfo = await NarakaApiClient.GetUserInfoAsync(roleId, ct).ConfigureAwait(false);
            if (userInfo?.Code != 200 || userInfo.Data == null)
            {
                result.Failed = true;
                return result;
            }

            var d = userInfo.Data;
            result.UserName = d.Role?.RoleName ?? d.NickName ?? userName;
            result.Level = "Lv." + (d.Role?.RoleLevel ?? 0).ToString();
            result.UID = d.Role?.Uid ?? string.Empty;
            result.AvatarUrl = d.Role?.HeadIcon ?? string.Empty;
            result.SoloRankScore = d.SurviveSingleGrade ?? 0;
            result.DuoRankScore = d.SurviveDoubleGrade ?? 0;
            result.TrioRankScore = d.SurviveTriplexGrade ?? 0;

            // 表达式与原 VM 完全一致：generated client 的 seasonId 签名（int 或 int?）通过隐式转换匹配。
            var seasonId = selectedSeasonCode ?? d.CurrentSeasonId;
            var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
            result.Stats = await _statsLoader.LoadAsync(roleId, seasonId, gameMode, ct).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>
    /// TeamMemberLoader 的返回 DTO。VM 把字段映射到 TeamMemberInfo 上。
    /// </summary>
    public class MemberLoadResult
    {
        public bool Failed { get; set; }
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
