using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    public class SignatureTicketProviderTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            public int Calls;
            public Func<HttpRequestMessage, HttpResponseMessage> Responder = _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"code":200,"data":{"appId":"naraka-h5","appSecret":"S","expireTime":9999999999999}}""",
                        Encoding.UTF8,
                        "application/json"),
                };

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(Responder(request));
            }
        }

        private static HttpClient CreateClient(StubHandler h) =>
            new(h) { BaseAddress = new Uri("https://naraka.drivod.top") };

        [Fact]
        public async Task GetAsync_CachesUntilExpiryLead()
        {
            var handler = new StubHandler();
            var now = DateTimeOffset.FromUnixTimeMilliseconds(1_000_000_000_000L);
            var provider = new SignatureTicketProvider(CreateClient(handler), () => now);

            var a = await provider.GetAsync();
            var b = await provider.GetAsync();

            Assert.Same(a, b);
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task GetAsync_RefreshesWhenWithinLeadTimeOfExpiry()
        {
            var handler = new StubHandler();
            long expire = 2_000_000_000_000L;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(1_000_000_000_000L);
            handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"code\":200,\"data\":{\"appId\":\"naraka-h5\",\"appSecret\":\"S\",\"expireTime\":" + expire + "}}",
                    Encoding.UTF8,
                    "application/json"),
            };
            DateTimeOffset Clock() => now;
            var provider = new SignatureTicketProvider(CreateClient(handler), Clock);

            await provider.GetAsync();

            // 前进到 expireTime - 5s (< leadTime 10s) → 触发刷新
            now = DateTimeOffset.FromUnixTimeMilliseconds(expire - 5_000L);
            await provider.GetAsync();

            Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Invalidate_ForcesNextGetToRefetch()
        {
            var handler = new StubHandler();
            var provider = new SignatureTicketProvider(CreateClient(handler), () => DateTimeOffset.UtcNow);
            await provider.GetAsync();
            provider.Invalidate();
            await provider.GetAsync();
            Assert.Equal(2, handler.Calls);
        }
    }
}
