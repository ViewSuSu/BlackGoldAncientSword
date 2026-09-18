using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Modules.UI.Stats.Services;

namespace BlackGoldAncientSword.Modules.UI.AuthChallenge.Services
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxSelfProfileResolver
    {
        private readonly IPlayerPrefsService _playerPrefs;
        private readonly PlayerStatsLoader _statsLoader;

        public HeyboxSelfProfileResolver(IPlayerPrefsService playerPrefs, PlayerStatsLoader statsLoader)
        {
            _playerPrefs = playerPrefs;
            _statsLoader = statsLoader;
        }

        public async Task<(string Nickname, string Avatar)> ResolveAsync(CancellationToken ct)
        {
            try
            {
                var prefs = _playerPrefs.Current;
                var localUid = prefs.PlayerId;
                if (string.IsNullOrWhiteSpace(localUid)) return (string.Empty, string.Empty);

                var search = await _statsLoader.SearchLocalPlayerAsync(localUid, prefs.PlayerName, ct).ConfigureAwait(false);
                if (search is null || string.IsNullOrEmpty(search.RoleIdSimple)) return (string.Empty, string.Empty);

                var ctx = new PlayerSourceContext(search.RoleIdSimple, search.Server);
                var info = await _statsLoader.FetchUserInfoAsync(ctx, ct).ConfigureAwait(false);
                if (info is null) return (string.Empty, string.Empty);

                return (info.RoleName ?? string.Empty, info.HeadIcon ?? string.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warning($"{nameof(HeyboxSelfProfileResolver)}.{nameof(ResolveAsync)}",
                    $"本机玩家资料解析失败，标题栏退回默认头像: {ex.Message}");
                return (string.Empty, string.Empty);
            }
        }
    }
}
