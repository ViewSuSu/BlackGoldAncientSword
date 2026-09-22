using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{

    [Component(ComponentLifetime.Singleton)]
    public class TeamMemberLoader
    {
        private const string GameType = "yjwj";

        private const int SearchPageSize = 20;

        private const string AnonymousPlayerName = "匿名玩家";

        private readonly PlayerStatsLoader _statsLoader;
        private readonly HeyboxHomeDataProvider _homeData;
        private readonly HeyboxRequestCache _cache;

        public TeamMemberLoader(PlayerStatsLoader statsLoader, HeyboxHomeDataProvider homeData, HeyboxRequestCache cache)
        {
            _statsLoader = statsLoader;
            _homeData = homeData;
            _cache = cache;
        }

        private Task<Framework.Http.Generated.HeyboxSearchResponse> SearchPlayersAsync(
            string keyword, CancellationToken ct)
            => _cache.RunAsync(
                $"search|{GameType}|{keyword}|0|{SearchPageSize}",
                () => NarakaApiClient.SearchPlayersAsync(
                    gameType: GameType, q: keyword, offset: 0, limit: SearchPageSize, ct: CancellationToken.None),
                ct);

        public async Task<MemberLoadResult> LoadAsync(
            string userName,
            string? selectedSeasonKey,
            GameModeCategory category,
            TeamSize teamSize,
            CancellationToken ct,
            string? localUidOverride = null)
        {
            var result = new MemberLoadResult();

            try
            {
                PlayerSourceContext? ctx;
                string? searchMsg;
                if (LooksLikeRoleId(localUidOverride))
                {
                    ctx = PlayerSourceContext.FromRoleId(localUidOverride!);
                    searchMsg = null;
                }
                else
                {
                    (ctx, searchMsg) = await ResolveByNameAsync(userName, ct).ConfigureAwait(false);
                }

                if (ctx is null)
                {
                    result.Failed = true;
                    result.FailMsg = searchMsg;
                    return result;
                }

                result.SourceContext = ctx;

                var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
                var battleTid = gameMode.ToHeyBoxBattleTid().ToString(System.Globalization.CultureInfo.InvariantCulture);

                // 只查一次就返回，把"补数据"交给调用方在数据上屏之后做（见 NeedsFollowUp）。
                // 原先这里是个最多 3 轮的重试循环，中间还有 5s + 3s 的 Task.Delay——意味着
                // 玩家名/统计没拿全时，整张卡要空白转圈 8 秒才显示任何东西，体验很差。
                var (userInfo, _, waitUpdate) = await FetchProfileAsync(ctx, selectedSeasonKey, battleTid, ct).ConfigureAwait(false);
                var stats = await _statsLoader.LoadAsync(ctx, selectedSeasonKey, gameMode, ct).ConfigureAwait(false);

                result.UserName = ResolveMemberName(userInfo, userName);
                result.Level = userInfo is null || userInfo.RoleLevel <= 0 ? string.Empty : "LV." + userInfo.RoleLevel;
                result.UID = string.IsNullOrEmpty(userInfo?.Uid) ? ctx.RoleId : userInfo!.Uid;
                result.AvatarUrl = userInfo?.HeadIcon ?? string.Empty;
                result.SoloRankScore = 0;
                result.DuoRankScore = 0;
                result.TrioRankScore = 0;

                result.SourceContextUserInfo = userInfo;
                result.Stats = stats;
                result.WaitUpdate = waitUpdate;
                return result;
            }
            catch (NarakaApiException ex)
            {
                result.Failed = true;
                result.FailMsg = ex.Msg;
                return result;
            }
        }

        /// <summary>
        /// 首查结果是否值得后台再追一次：名字还是"匿名玩家"（服务端当时还没算完），
        /// 或者统计一项都没有。追的时候会先 invalidate 掉 <c>home/data</c> 缓存，
        /// 否则第二次拿到的还是同一份空数据。
        /// </summary>
        public static bool NeedsFollowUp(UnifiedUserInfo? userInfo, PlayerStatsLoadResult? stats)
        {
            if (!HasRealName(userInfo?.RoleName)) return true;
            return stats is null || stats.Stats.Count == 0;
        }

        /// <summary>后台补数据的等待间隔（按轮次）：前两次 3s、最后一次 4s。</summary>
        private static readonly int[] FollowUpDelaySeconds = { 3, 3, 4 };

        /// <summary>后台最多补几次：直接由间隔表长度决定，避免次数和间隔两处各自漂移。</summary>
        public static int MaxFollowUpRounds => FollowUpDelaySeconds.Length;

        /// <summary>第 <paramref name="round"/> 轮（从 0 计）发起前的等待时长。</summary>
        public static TimeSpan FollowUpDelay(int round)
        {
            var index = Math.Clamp(round, 0, FollowUpDelaySeconds.Length - 1);
            return TimeSpan.FromSeconds(FollowUpDelaySeconds[index]);
        }

        public async Task<PlayerStatsLoadResult?> LoadStatsOnlyAsync(
            PlayerSourceContext ctx,
            string? selectedSeasonKey,
            GameModeCategory category,
            TeamSize teamSize,
            CancellationToken ct)
        {
            var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
            return await _statsLoader.LoadAsync(ctx, selectedSeasonKey, gameMode, ct).ConfigureAwait(false);
        }

        private static string ResolveMemberName(UnifiedUserInfo? userInfo, string userName)
        {
            if (!string.IsNullOrEmpty(userInfo?.RoleName)) return userInfo!.RoleName;
            return LooksLikeRoleId(userName) ? string.Empty : userName;
        }

        public void InvalidateProfile(
            PlayerSourceContext ctx, string? seasonKey, GameModeCategory category, TeamSize teamSize)
        {
            var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
            var battleTid = gameMode.ToHeyBoxBattleTid().ToString(System.Globalization.CultureInfo.InvariantCulture);
            _homeData.Invalidate(ctx.RoleId, ctx.Server, seasonKey, battleTid);
        }

        public static bool HasRealName(string? name)
            => !string.IsNullOrWhiteSpace(name)
               && !string.Equals(name, AnonymousPlayerName, StringComparison.Ordinal);

        /// <summary>
        /// 后台补数据：先 invalidate 掉 <c>home/data</c> 缓存（否则拿到的还是同一份空结果），
        /// 再重拉一次资料 + 统计，供调用方按"有什么更新什么"写回卡片。
        /// </summary>
        public async Task<(UnifiedUserInfo? UserInfo, PlayerStatsLoadResult? Stats)> RefetchProfileAndStatsAsync(
            PlayerSourceContext ctx,
            string? selectedSeasonKey,
            GameModeCategory category,
            TeamSize teamSize,
            CancellationToken ct)
        {
            var gameMode = GameModeExtensions.FromCategoryAndTeamSize(category, teamSize);
            var battleTid = gameMode.ToHeyBoxBattleTid().ToString(System.Globalization.CultureInfo.InvariantCulture);

            _homeData.InvalidatePlayer(ctx.RoleId);

            var (userInfo, _, _) = await FetchProfileAsync(ctx, selectedSeasonKey, battleTid, ct).ConfigureAwait(false);
            var stats = await _statsLoader.LoadAsync(ctx, selectedSeasonKey, gameMode, ct).ConfigureAwait(false);
            return (userInfo, stats);
        }

        private async Task<(UnifiedUserInfo? userInfo, string? msg, bool waitUpdate)> FetchProfileAsync(
            PlayerSourceContext ctx, string? seasonKey, string battleTid, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, seasonKey, battleTid, ct).ConfigureAwait(false);

            BlackGoldAncientSword.Framework.Core.Infrastructure.DiagLog.Write("TeamLoader",
                $"profile响应: uid={ctx.RoleId} home={(home is null ? "NULL" : "ok")} isSuccess={home?.IsSuccess} " +
                $"result={(home?.Result is null ? "NULL" : "ok")} playerInfo={(home?.Result?.PlayerInfo is null ? "NULL" : "ok")}");

            return (UnifiedMapper.MapPlayerInfo(home?.Result), home?.Msg, home?.Result?.WaitUpdate == true);
        }

        private async Task<(PlayerSourceContext? ctx, string? msg)> ResolveByNameAsync(
            string userName, CancellationToken ct)
        {
            var resp = await SearchPlayersAsync(userName, ct).ConfigureAwait(false);
            var search = UnifiedMapper.MapSearch(resp);
            return search is null || string.IsNullOrEmpty(search.RoleIdSimple)
                ? (null, resp?.Msg)
                : (new PlayerSourceContext(search.RoleIdSimple, search.Server), resp?.Msg);
        }

        public Task<(List<UnifiedSeason> Seasons, string? CurrentSeasonKey)> FetchSeasonsAsync(
            PlayerSourceContext ctx, CancellationToken ct)
            => _statsLoader.FetchSeasonsAsync(ctx, ct);

        public async Task<PlayerSourceContext?> ResolveLocalPlayerContextAsync(
            string? localRoleId, string localName, CancellationToken ct)
        {
            if (LooksLikeRoleId(localRoleId))
                return PlayerSourceContext.FromRoleId(localRoleId!);

            if (string.IsNullOrWhiteSpace(localName)) return null;

            var (ctx, _) = await ResolveByNameAsync(localName, ct).ConfigureAwait(false);
            return ctx;
        }

        private static bool LooksLikeRoleId(string? value)
            => !string.IsNullOrWhiteSpace(value) && value.Length == 22 && value.All(char.IsAsciiLetterOrDigit);
    }

    public class MemberLoadResult
    {
        public bool Failed { get; set; }

        public string? FailMsg { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string UID { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public double SoloRankScore { get; set; }
        public double DuoRankScore { get; set; }
        public double TrioRankScore { get; set; }

        public PlayerSourceContext? SourceContext { get; set; }

        /// <summary>
        /// 首查拿到的资料原文。调用方用它判断名字是否还是"匿名玩家"、
        /// 决定要不要起后台补齐（<see cref="TeamMemberLoader.NeedsFollowUp"/>）。
        /// </summary>
        public UnifiedUserInfo? SourceContextUserInfo { get; set; }

        public PlayerStatsLoadResult? Stats { get; set; }

        public bool WaitUpdate { get; set; }
    }
}
