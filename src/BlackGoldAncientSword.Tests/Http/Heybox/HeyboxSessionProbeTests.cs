using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxSessionProbeTests
    {
        [Theory]
        [InlineData("ok", true, true)]
        [InlineData("login", false, false)]
        [InlineData("relogin", false, false)]
        [InlineData("failed", true, false)]
        public async Task ProbeAsync_Should_Judge_By_Server_Status(string status, bool alive, bool carriesHome)
        {
            NarakaApiClient.Configure(new StubHandler($$$"""{"status":"{{{status}}}","msg":"","result":{"seasons":[]}}"""));

            var outcome = await new HeyboxSessionProbe().ProbeAsync("l77c000015949400120163", "163");

            Assert.Equal(alive, outcome.Alive);
            Assert.Equal(carriesHome, outcome.Home is not null);
        }

        [Fact]
        public async Task ProbeAsync_Should_Keep_Session_When_Request_Fails()
        {
            NarakaApiClient.Configure(new StubHandler(null));

            var outcome = await new HeyboxSessionProbe().ProbeAsync("l77c000015949400120163", "163");

            Assert.True(outcome.Alive);
            Assert.Null(outcome.Home);
        }

        [Fact]
        public async Task ProbeAsync_Should_Not_Request_Without_RoleId()
        {
            var handler = new StubHandler("""{"status":"login","msg":""}""");
            NarakaApiClient.Configure(handler);

            var outcome = await new HeyboxSessionProbe().ProbeAsync(string.Empty, "163");

            Assert.True(outcome.Alive);
            Assert.Equal(0, handler.Calls);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string? _body;

            public StubHandler(string? body) => _body = body;

            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;

                if (_body is null)
                    throw new HttpRequestException("boom");

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                };

                return Task.FromResult(response);
            }
        }
    }
}
