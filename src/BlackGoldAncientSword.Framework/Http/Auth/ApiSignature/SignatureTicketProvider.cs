using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    /// <summary>
    /// 与 JS 侧 <c>O7/k7</c> 对齐：缓存 ticket，到期前 10s 提前刷新，单飞控制。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class SignatureTicketProvider : ISignatureTicketProvider
    {
        private readonly HttpClient _bareClient;
        private readonly Func<DateTimeOffset> _clock;

        private SignatureTicket? _cached;
        private Task<SignatureTicket>? _inflight;
        private readonly object _sync = new();

        public SignatureTicketProvider() : this(CreateDefaultClient(), () => DateTimeOffset.UtcNow) { }

        internal SignatureTicketProvider(HttpClient bareClient, Func<DateTimeOffset> clock)
        {
            _bareClient = bareClient;
            _clock = clock;
        }

        public async Task<SignatureTicket> GetAsync(CancellationToken ct = default)
        {
            var cached = _cached;
            var nowMs = _clock().ToUnixTimeMilliseconds();
            if (cached != null && cached.ExpireTime - SignatureConstants.TicketRefreshLeadTimeMs > nowMs)
                return cached;

            Task<SignatureTicket> inflight;
            lock (_sync)
            {
                cached = _cached;
                nowMs = _clock().ToUnixTimeMilliseconds();
                if (cached != null && cached.ExpireTime - SignatureConstants.TicketRefreshLeadTimeMs > nowMs)
                    return cached;

                inflight = _inflight ??= FetchAsync(ct)
                    .ContinueWith(t =>
                    {
                        lock (_sync)
                        {
                            _inflight = null;
                            if (t.Status == TaskStatus.RanToCompletion)
                                _cached = t.Result;
                        }
                        return t.GetAwaiter().GetResult();
                    }, TaskScheduler.Default);
            }

            return await inflight.ConfigureAwait(false);
        }

        public void Invalidate()
        {
            lock (_sync) _cached = null;
        }

        private async Task<SignatureTicket> FetchAsync(CancellationToken ct)
        {
            var uri = new Uri(SignatureConstants.TicketPath + "?appId=" + SignatureConstants.AppId, UriKind.Relative);
            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            using var res = await _bareClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            var envelope = await res.Content.ReadFromJsonAsync<TicketEnvelope>(cancellationToken: ct).ConfigureAwait(false);
            if (envelope is null)
                throw new InvalidOperationException("签名票据响应为空");
            if (envelope.Code != 0 && envelope.Code != 200)
                throw new InvalidOperationException(envelope.Msg ?? "领取签名票据失败");

            var data = envelope.Data;
            if (data is null || string.IsNullOrEmpty(data.AppId) || string.IsNullOrEmpty(data.AppSecret) || data.ExpireTime <= 0)
                throw new InvalidOperationException("签名票据响应不完整");

            Debug.WriteLine($"[{nameof(SignatureTicketProvider)}] ticket refreshed, expireIn={data.ExpireTime - _clock().ToUnixTimeMilliseconds()}ms");
            return new SignatureTicket(data.AppId, data.AppSecret, data.ExpireTime);
        }

        private static HttpClient CreateDefaultClient()
        {
            var handler = new HttpClientHandler();
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://desktop.naraka.drivod.top"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            return client;
        }

        private sealed class TicketEnvelope
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("msg")] public string? Msg { get; set; }
            [JsonPropertyName("data")] public TicketData? Data { get; set; }
        }

        private sealed class TicketData
        {
            [JsonPropertyName("appId")] public string AppId { get; set; } = string.Empty;
            [JsonPropertyName("appSecret")] public string AppSecret { get; set; } = string.Empty;
            [JsonPropertyName("expireTime")] public long ExpireTime { get; set; }
        }
    }
}
