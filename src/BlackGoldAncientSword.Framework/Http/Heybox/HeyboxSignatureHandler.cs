using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public sealed class HeyboxSignatureHandler : DelegatingHandler
    {
        private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/game/player_search/do",
        };

        private static readonly HashSet<string> NativeSignedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/account/bind_game_id",
            "/account/unbind_game_id",
        };

        private readonly IHeyboxSessionState _session;
        private readonly Func<long> _unixSeconds;
        private readonly Func<string> _newNonce;
        private readonly HeyboxNativeClientProfile? _profile;

        public HeyboxSignatureHandler(IHeyboxSessionState session)
            : this(session, static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds(), HeyboxHkey.NewNonce, null) { }

        public HeyboxSignatureHandler(IHeyboxSessionState session, HeyboxNativeClientProfile? profile)
            : this(session, static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds(), HeyboxHkey.NewNonce, profile) { }

        public HeyboxSignatureHandler(IHeyboxSessionState session, Func<long> unixSeconds, Func<string> newNonce)
            : this(session, unixSeconds, newNonce, null) { }

        public HeyboxSignatureHandler(
            IHeyboxSessionState session,
            Func<long> unixSeconds,
            Func<string> newNonce,
            HeyboxNativeClientProfile? profile)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _unixSeconds = unixSeconds ?? throw new ArgumentNullException(nameof(unixSeconds));
            _newNonce = newNonce ?? throw new ArgumentNullException(nameof(newNonce));
            _profile = profile;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.RequestUri is { } uri && !AnonymousPaths.Contains(uri.AbsolutePath))
            {
                var query = ParseQuery(uri.Query);
                var session = _session.Current?.Session;

                var native = session is not null
                             && _profile is not null
                             && NativeSignedPaths.Contains(uri.AbsolutePath.TrimEnd('/'));

                if (native)
                {
                    ApplyNativeParams(query, uri.AbsolutePath, session!, _profile!);
                }
                else
                {
                    ApplyWebParams(query, uri.AbsolutePath, session);
                }

                if (session is not null && !request.Headers.Contains("Cookie"))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
                }

                request.RequestUri = Rebuild(uri, query);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private void ApplyNativeParams(
            Dictionary<string, string> query,
            string path,
            HeyboxSession session,
            HeyboxNativeClientProfile profile)
        {
            var seconds = _unixSeconds();
            var nonce = HeyboxNativeHkey.NewNonce();

            query[HeyboxRequestParams.HeyboxId] = session.HeyboxId;
            query["imei"] = profile.Imei;
            query["device_info"] = profile.DeviceInfo;
            query["nonce"] = nonce;
            query["hkey"] = HeyboxNativeHkey.Compute(path, seconds, profile.Imei, session.HeyboxId);
            query["_rnd"] = HeyboxNativeHkey.NewRoundToken();
            query["os_type"] = profile.OsType;
            query["x_os_type"] = profile.OsType;
            query["x_client_type"] = profile.XClientType;
            query["os_version"] = profile.OsVersion;
            query["version"] = profile.Version;
            query["build"] = profile.Build;
            query[HeyboxRequestParams.Time] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            query["dw"] = profile.Dw;
            query["channel"] = profile.Channel;
            query["x_app"] = HeyboxRequestParams.XApp;
            query["time_zone"] = profile.TimeZone;
        }

        private void ApplyWebParams(Dictionary<string, string> query, string path, HeyboxSession? session)
        {
            AddIfAbsent(query, "app", HeyboxRequestParams.App);
            AddIfAbsent(query, "os_type", HeyboxRequestParams.OsType);
            AddIfAbsent(query, "x_app", HeyboxRequestParams.XApp);
            AddIfAbsent(query, "x_client_type", HeyboxRequestParams.XClientType);
            AddIfAbsent(query, "x_os_type", HeyboxRequestParams.XOsType);
            AddIfAbsent(query, "x_client_version", HeyboxRequestParams.XClientVersion);
            AddIfAbsent(query, "version", HeyboxRequestParams.Version);
            AddIfAbsent(query, HeyboxRequestParams.Server, HeyboxRequestParams.DefaultServer);

            if (session is not null)
            {
                AddIfAbsent(query, HeyboxRequestParams.HeyboxId, session.HeyboxId);
                AddIfAbsent(query, HeyboxRequestParams.UserId, session.HeyboxId);
            }

            var seconds = _unixSeconds();
            var nonce = _newNonce();
            query[HeyboxRequestParams.Time] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            query[HeyboxRequestParams.Nonce] = nonce;
            query[HeyboxRequestParams.Hkey] = HeyboxHkey.Compute(path, seconds, nonce);
        }

        private static void AddIfAbsent(Dictionary<string, string> query, string key, string value)
        {
            if (!query.ContainsKey(key)) query[key] = value;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(query)) return result;

            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var key = Uri.UnescapeDataString(pair[..eq]);
                var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                result[key] = value;
            }

            return result;
        }

        private static Uri Rebuild(Uri uri, Dictionary<string, string> query)
        {
            var sb = new System.Text.StringBuilder(uri.GetLeftPart(UriPartial.Path));
            if (query.Count > 0)
            {
                sb.Append('?');
                var first = true;
                foreach (var kv in query)
                {
                    if (!first) sb.Append('&');
                    first = false;
                    sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
                }
            }

            return new Uri(sb.ToString());
        }
    }
}
