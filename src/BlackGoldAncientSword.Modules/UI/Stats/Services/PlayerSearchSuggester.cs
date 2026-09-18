using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Modules.UI.Stats.Services
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class PlayerSearchSuggester
    {
        private const string GameType = "yjwj";

        private readonly HeyboxRequestCache _cache;

        public PlayerSearchSuggester(HeyboxRequestCache cache)
        {
            _cache = cache;
        }

        public async Task<List<UnifiedSearchResult>> FetchAsync(
            string keyword, int offset, int limit, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(keyword) || limit <= 0) return new List<UnifiedSearchResult>();

            try
            {
                var resp = await _cache.RunAsync(
                    $"search|{GameType}|{keyword}|{offset}|{limit}",
                    () => NarakaApiClient.SearchPlayersAsync(
                        gameType: GameType, q: keyword, offset: offset, limit: limit, ct: CancellationToken.None),
                    ct).ConfigureAwait(false);

                return UnifiedMapper.MapSearchList(resp);
            }
            catch (OperationCanceledException) { throw; }
            catch (NarakaApiException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerSearchSuggester)}.{nameof(FetchAsync)}");
                return new List<UnifiedSearchResult>();
            }
        }
    }
}
