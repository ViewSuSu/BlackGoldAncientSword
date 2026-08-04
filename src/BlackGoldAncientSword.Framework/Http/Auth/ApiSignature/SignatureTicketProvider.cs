using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

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
            // 签名票据是**所有** API（含拉验证码）的前置：这一步失败会让后续请求签不了名/发不出，
            // 表象是"验证码加载失败"，真根因却在此。故每个失败点都先记明"票据领取失败 + 原因"再抛，
            // 避免根因被下游的 AjCaptchaService 误标成"验证码网络故障"。
            const string src = nameof(SignatureTicketProvider) + "." + nameof(FetchAsync);
            var url = SignatureConstants.TicketPath + "?appId=" + SignatureConstants.AppId;
            try
            {
                var uri = new Uri(url, UriKind.Relative);
                using var req = new HttpRequestMessage(HttpMethod.Post, uri);
                using var res = await _bareClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    AppLog.Warning(src, $"ticket HTTP {(int)res.StatusCode} {res.StatusCode} url={url}");
                    res.EnsureSuccessStatusCode(); // 保持原有抛出语义
                }

                var envelope = await res.Content.ReadFromJsonAsync<TicketEnvelope>(cancellationToken: ct).ConfigureAwait(false);
                if (envelope is null)
                {
                    AppLog.Warning(src, "ticket response empty");
                    throw new InvalidOperationException("签名票据响应为空");
                }
                if (envelope.Code != 0 && envelope.Code != 200)
                {
                    AppLog.Warning(src, $"ticket backend rejected code={envelope.Code} msg={envelope.Msg}");
                    throw new InvalidOperationException(envelope.Msg ?? "领取签名票据失败");
                }

                var data = envelope.Data;
                if (data is null || string.IsNullOrEmpty(data.AppId) || string.IsNullOrEmpty(data.AppSecret) || data.ExpireTime <= 0)
                {
                    AppLog.Warning(src, $"ticket payload incomplete: data={data is not null} appId={!string.IsNullOrEmpty(data?.AppId)} appSecret={!string.IsNullOrEmpty(data?.AppSecret)} expireTime={data?.ExpireTime}");
                    throw new InvalidOperationException("签名票据响应不完整");
                }

                Debug.WriteLine($"[{nameof(SignatureTicketProvider)}] ticket refreshed, expireIn={data.ExpireTime - _clock().ToUnixTimeMilliseconds()}ms");
                return new SignatureTicket(data.AppId, data.AppSecret, data.ExpireTime);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 调用方主动取消，不是故障
            }
            catch (InvalidOperationException)
            {
                throw; // 上面已记明具体原因，直接上抛避免重复日志
            }
            catch (Exception ex)
            {
                // 网络层失败（超时/拒连/DNS/TLS）——签名服务连不上，是"验证码加载失败"最隐蔽的根因。
                AppLog.Warning(src, $"ticket fetch network failure url={url}: {DescribeException(ex)}");
                throw;
            }
        }

        /// <summary>展开异常链取最内层——HttpRequestException.Message 常笼统，真正根因在 InnerException（SocketException 等）。</summary>
        private static string DescribeException(Exception ex)
        {
            var inner = ex;
            while (inner.InnerException is not null)
                inner = inner.InnerException;
            return ReferenceEquals(inner, ex)
                ? $"{ex.GetType().Name}: {ex.Message}"
                : $"{ex.GetType().Name}: {ex.Message} -> {inner.GetType().Name}: {inner.Message}";
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
