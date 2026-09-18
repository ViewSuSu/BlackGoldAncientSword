using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public static class HeyboxSessionProbe
    {
        public static async Task<bool> IsSessionAliveAsync(string roleId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(roleId)) return true;

            try
            {
                var raw = await NarakaApiClient.Http
                    .GetStringAsync($"/game/yjwj/home/data?role_id={Uri.EscapeDataString(roleId)}", ct)
                    .ConfigureAwait(false);

                using var doc = JsonDocument.Parse(raw);
                var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;

                return !string.Equals(status, "login", StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(status, "relogin", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
