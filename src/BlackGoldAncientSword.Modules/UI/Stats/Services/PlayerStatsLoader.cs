using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class PlayerStatsLoader
    {
        private const string GameType = "yjwj";

        private const int SearchPageSize = 20;

        private const string DefaultServer = "163";

        private readonly HeyboxHomeDataProvider _homeData;

        private readonly HeyboxRequestCache _cache;

        public PlayerStatsLoader(HeyboxHomeDataProvider homeData, HeyboxRequestCache cache)
        {
            _homeData = homeData;
            _cache = cache;
        }

        private Task<HeyboxSearchResponse> SearchAsync(string keyword, CancellationToken ct)
            => _cache.RunAsync(
                $"search|{GameType}|{keyword}|0|{SearchPageSize}",
                () => NarakaApiClient.SearchPlayersAsync(
                    gameType: GameType, q: keyword, offset: 0, limit: SearchPageSize, ct: CancellationToken.None),
                ct);

        public async Task<UnifiedSearchResult?> SearchRoleByNameAsync(string playerName, CancellationToken ct)
        {
            try
            {
                var resp = await SearchAsync(playerName, ct).ConfigureAwait(false);
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

        public async Task<UnifiedSearchResult?> SearchLocalPlayerAsync(
            string? localRoleId, string localName, CancellationToken ct)
        {
            try
            {
                var resp = await SearchAsync(localName, ct).ConfigureAwait(false);
                var byName = UnifiedMapper.MapSearch(resp, localRoleId);
                if (byName is not null) return byName;

                if (!LooksLikeRoleId(localName)) return null;

                var nickname = await ResolveNicknameAsync(localName, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(nickname) ||
                    string.Equals(nickname, localName, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var retry = await SearchAsync(nickname, ct).ConfigureAwait(false);

                var exact = retry?.Result?.PlayerList?.FirstOrDefault(
                    p => string.Equals(p.GameId, localName, StringComparison.OrdinalIgnoreCase));

                return exact is null ? null : UnifiedMapper.MapSearch(retry, localName);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(SearchLocalPlayerAsync)}");
                return null;
            }
        }

        private async Task<string?> ResolveNicknameAsync(string roleId, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(roleId, DefaultServer, season: null, battleTid: null, ct)
                .ConfigureAwait(false);
            return home?.Result?.PlayerInfo?.Name;
        }

        private static bool LooksLikeRoleId(string value) =>
            value.Length == 22 && value.All(char.IsAsciiLetterOrDigit);

        public async Task<UnifiedUserInfo?> FetchUserInfoAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, season: null, battleTid: null, ct).ConfigureAwait(false);
            return UnifiedMapper.MapPlayerInfo(home?.Result);
        }

        public async Task<bool> IsWaitingUpdateAsync(PlayerSourceContext ctx, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, season: null, battleTid: null, ct).ConfigureAwait(false);
            return home?.Result?.WaitUpdate == true;
        }

        public void InvalidatePlayer(PlayerSourceContext ctx)
            => _homeData.InvalidatePlayer(ctx.RoleId);

        public async Task<(List<UnifiedSeason> Seasons, string? CurrentSeasonKey)> FetchSeasonsAsync(
            PlayerSourceContext ctx, CancellationToken ct)
        {
            try
            {
                var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, season: null, battleTid: null, ct).ConfigureAwait(false);
                return (UnifiedMapper.MapSeasons(home?.Result?.Seasons), home?.Result?.Season);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(FetchSeasonsAsync)}");
                return (new List<UnifiedSeason>(), null);
            }
        }

        public async Task<UnifiedPlayerStats?> FetchPlayerStatsAsync(
            PlayerSourceContext ctx, string? seasonKey, GameMode gameMode, CancellationToken ct)
        {
            var battleTid = gameMode.ToHeyBoxBattleTid().ToString(CultureInfo.InvariantCulture);

            try
            {
                var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, seasonKey, battleTid, ct).ConfigureAwait(false);
                return UnifiedMapper.MapSeasonSummary(home?.Result);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(FetchPlayerStatsAsync)}", "API call failed");
                return null;
            }
        }

        public async Task<UnifiedBattleDetail?> FetchBattleDetailAsync(PlayerSourceContext ctx, string detailKey, CancellationToken ct)
        {
            try
            {
                var resp = await _cache.RunAsync(
                    $"matchdetail|{detailKey}",
                    () => NarakaApiClient.GetMatchDetailAsync(matchId: detailKey, ct: CancellationToken.None),
                    ct).ConfigureAwait(false);
                return UnifiedMapper.MapMatchDetail(resp);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerStatsLoader)}.{nameof(FetchBattleDetailAsync)}");
                return null;
            }
        }
    }
}
