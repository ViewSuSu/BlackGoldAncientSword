using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxSessionProbe : IHeyboxSessionProbe
    {

        public async Task<HeyboxSessionProbeOutcome> ProbeAsync(
            string roleId, string server, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(roleId))
                return new HeyboxSessionProbeOutcome(true, null);

            try
            {
                var url = $"/game/yjwj/home/data?role_id={Uri.EscapeDataString(roleId)}";
                if (!string.IsNullOrEmpty(server))
                    url += $"&server={Uri.EscapeDataString(server)}";

                var raw = await NarakaApiClient.Http.GetStringAsync(url, ct).ConfigureAwait(false);
                var home = JsonSerializer.Deserialize<HeyboxHomeResponse>(raw, NarakaApiClient.JsonOptions);

                if (IsLoginFailure(home?.Status))
                    return new HeyboxSessionProbeOutcome(false, null);

                return new HeyboxSessionProbeOutcome(true, home is { IsSuccess: true } ? home : null);
            }
            catch (Exception)
            {
                return new HeyboxSessionProbeOutcome(true, null);
            }
        }

        private static bool IsLoginFailure(string? status) =>
            string.Equals(status, "login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "relogin", StringComparison.OrdinalIgnoreCase);
    }
}
