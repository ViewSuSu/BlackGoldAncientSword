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

        private const int MaxLoadAttempts = 3;

        private static readonly TimeSpan[] LoadRetryDelays =
        {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(3),
        };

        private const string DefaultServer = "163";

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
                    ctx = new PlayerSourceContext(localUidOverride!, ServerFromRoleId(localUidOverride!));
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

                UnifiedUserInfo? userInfo = null;
                PlayerStatsLoadResult? stats = null;

                for (var attempt = 1; ; attempt++)
                {
                    (userInfo, _) = await FetchProfileAsync(ctx, selectedSeasonKey, battleTid, ct).ConfigureAwait(false);
                    stats = await _statsLoader.LoadAsync(ctx, selectedSeasonKey, gameMode, ct).ConfigureAwait(false);

                    if (!NeedsRetry(userInfo, stats) || attempt >= MaxLoadAttempts) break;

                    _homeData.Invalidate(ctx.RoleId, ctx.Server, selectedSeasonKey, battleTid);
                    await Task.Delay(LoadRetryDelays[attempt - 1], ct).ConfigureAwait(false);
                }

                result.UserName = ResolveMemberName(userInfo, userName);
                result.Level = userInfo is null || userInfo.RoleLevel <= 0 ? string.Empty : "LV." + userInfo.RoleLevel;
                result.UID = string.IsNullOrEmpty(userInfo?.Uid) ? ctx.RoleId : userInfo!.Uid;
                result.AvatarUrl = userInfo?.HeadIcon ?? string.Empty;
                result.SoloRankScore = 0;
                result.DuoRankScore = 0;
                result.TrioRankScore = 0;

                result.Stats = stats;
                return result;
            }
            catch (NarakaApiException ex)
            {
                result.Failed = true;
                result.FailMsg = ex.Msg;
                return result;
            }
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

        private static bool NeedsRetry(UnifiedUserInfo? userInfo, PlayerStatsLoadResult? stats)
        {
            if (!HasRealName(userInfo?.RoleName)) return true;
            return stats is null || stats.Stats.Count == 0;
        }

        private async Task<(UnifiedUserInfo? userInfo, string? msg)> FetchProfileAsync(
            PlayerSourceContext ctx, string? seasonKey, string battleTid, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, seasonKey, battleTid, ct).ConfigureAwait(false);

            BlackGoldAncientSword.Framework.Core.Infrastructure.DiagLog.Write("TeamLoader",
                $"profile响应: uid={ctx.RoleId} home={(home is null ? "NULL" : "ok")} isSuccess={home?.IsSuccess} " +
                $"result={(home?.Result is null ? "NULL" : "ok")} playerInfo={(home?.Result?.PlayerInfo is null ? "NULL" : "ok")}");

            return (UnifiedMapper.MapPlayerInfo(home?.Result), home?.Msg);
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
                return new PlayerSourceContext(localRoleId!, ServerFromRoleId(localRoleId!));

            if (string.IsNullOrWhiteSpace(localName)) return null;

            var (ctx, _) = await ResolveByNameAsync(localName, ct).ConfigureAwait(false);
            return ctx;
        }

        private static bool LooksLikeRoleId(string? value)
            => !string.IsNullOrWhiteSpace(value) && value.Length == 22 && value.All(char.IsAsciiLetterOrDigit);

        private static string ServerFromRoleId(string roleId)
            => roleId.Length >= 3 && int.TryParse(roleId.AsSpan(roleId.Length - 3), out _)
                ? roleId.Substring(roleId.Length - 3)
                : DefaultServer;
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
        public PlayerStatsLoadResult? Stats { get; set; }
    }
}
