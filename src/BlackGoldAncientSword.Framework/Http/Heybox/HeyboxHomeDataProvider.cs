using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxHomeDataProvider
    {
        private readonly HeyboxRequestCache _cache;

        public HeyboxHomeDataProvider(HeyboxRequestCache cache)
        {
            _cache = cache;
        }

        public Task<HeyboxHomeResponse?> GetAsync(
            string roleId, string server, string? season, string? battleTid, CancellationToken ct)
        {
            return _cache.RunAsync<HeyboxHomeResponse?>(
                Key(roleId, server, season, battleTid),
                () => LoadAsync(roleId, server, season, battleTid),
                ct);
        }

        public void Seed(
            string roleId, string server, string? season, string? battleTid, HeyboxHomeResponse? response)
            => _cache.Set(Key(roleId, server, season, battleTid), response);

        public void Invalidate(string roleId, string server, string? season, string? battleTid)
            => _cache.Invalidate(Key(roleId, server, season, battleTid));

        public void InvalidatePlayer(string roleId)
            => _cache.InvalidatePrefix($"home|{roleId}|");

        private static string Key(string roleId, string server, string? season, string? battleTid)
            => $"home|{roleId}|{server}|{season}|{battleTid}";

        private static async Task<HeyboxHomeResponse?> LoadAsync(
            string roleId, string server, string? season, string? battleTid)
        {
            return await NarakaApiClient.GetPlayerHomeAsync(
                server: server,
                roleId: roleId,
                season: season,
                battleTid: battleTid,
                ct: CancellationToken.None).ConfigureAwait(false);
        }
    }
}
