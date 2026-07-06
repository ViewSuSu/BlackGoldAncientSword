using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    public class SignatureHandlerTests
    {
        private sealed class StubTicketProvider : ISignatureTicketProvider
        {
            public SignatureTicket Ticket = new("naraka-h5", "S", 9_999_999_999_999);
            public int Calls;
            public Task<SignatureTicket> GetAsync(CancellationToken ct = default)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(Ticket);
            }
            public void Invalidate() { }
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        [Fact]
        public async Task Send_AttachesSignatureHeaders()
        {
            var provider = new StubTicketProvider();
            var inner = new CaptureHandler();
            var handler = new SignatureHandler(provider, () => 1_735_689_600_000L, () => "0123456789abcdef01234567")
            {
                InnerHandler = inner,
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://x/") };

            using var res = await client.GetAsync("/api?a=1");

            Assert.NotNull(inner.LastRequest);
            Assert.True(inner.LastRequest!.Headers.Contains("appId"));
            Assert.True(inner.LastRequest.Headers.Contains("timestamp"));
            Assert.True(inner.LastRequest.Headers.Contains("nonce"));
            Assert.True(inner.LastRequest.Headers.Contains("sign"));
        }
    }
}
