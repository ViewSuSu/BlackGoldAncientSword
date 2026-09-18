using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public sealed class HeyboxSignatureHandler : DelegatingHandler
    {
        private readonly IHeyboxSessionState _session;
        private readonly Func<long> _unixSeconds;
        private readonly Func<string> _newNonce;

        public HeyboxSignatureHandler(IHeyboxSessionState session)
            : this(session, static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds(), HeyboxHkey.NewNonce) { }

        public HeyboxSignatureHandler(IHeyboxSessionState session, Func<long> unixSeconds, Func<string> newNonce)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _unixSeconds = unixSeconds ?? throw new ArgumentNullException(nameof(unixSeconds));
            _newNonce = newNonce ?? throw new ArgumentNullException(nameof(newNonce));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.RequestUri is { } uri)
            {
                var query = ParseQuery(uri.Query);

                AddIfAbsent(query, "app", HeyboxRequestParams.App);
                AddIfAbsent(query, "os_type", HeyboxRequestParams.OsType);
                AddIfAbsent(query, "x_app", HeyboxRequestParams.XApp);
                AddIfAbsent(query, "x_client_type", HeyboxRequestParams.XClientType);
                AddIfAbsent(query, "x_os_type", HeyboxRequestParams.XOsType);
                AddIfAbsent(query, "x_client_version", HeyboxRequestParams.XClientVersion);
                AddIfAbsent(query, "version", HeyboxRequestParams.Version);
                AddIfAbsent(query, HeyboxRequestParams.Server, HeyboxRequestParams.DefaultServer);

                var session = _session.Current?.Session;
                if (session is not null)
                {
                    AddIfAbsent(query, HeyboxRequestParams.HeyboxId, session.HeyboxId);
                    AddIfAbsent(query, HeyboxRequestParams.UserId, session.HeyboxId);

                    if (!request.Headers.Contains("Cookie"))
                        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
                }

                var seconds = _unixSeconds();
                var nonce = _newNonce();
                query[HeyboxRequestParams.Time] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                query[HeyboxRequestParams.Nonce] = nonce;
                query[HeyboxRequestParams.Hkey] = HeyboxHkey.Compute(uri.AbsolutePath, seconds, nonce);

                request.RequestUri = Rebuild(uri, query);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
