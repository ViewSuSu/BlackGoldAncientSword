using System;
using System.Text.Json.Serialization;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public sealed record HeyboxSession(string HeyboxId, string Pkey, long ExpireAt = 0)
    {
        [JsonIgnore]
        public DateTimeOffset? ExpiresAt =>
            ExpireAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(ExpireAt).ToLocalTime() : null;

        [JsonIgnore]
        public string CookieHeader => $"user_heybox_id={HeyboxId}; user_pkey={Pkey}";
    }
}
