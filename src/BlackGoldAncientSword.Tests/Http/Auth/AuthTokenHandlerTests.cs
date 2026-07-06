using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Moq;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    public class AuthTokenHandlerTests
    {
        private sealed class InlineTokenState : IAuthTokenState
        {
            public AuthToken? Current { get; private set; }
            public event EventHandler<AuthToken?>? Changed;
            public void Set(AuthToken? token) { Current = token; Changed?.Invoke(this, token); }
        }

        private sealed class InlineTokenStore : IAuthTokenStore
        {
            public AuthToken? Saved;
            public bool Cleared;
            public AuthToken? Load() => Saved;
            public void Save(AuthToken token) { Saved = token; }
            public void Clear() { Saved = null; Cleared = true; }
        }

        private sealed class QueuedHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            public List<HttpRequestMessage> Requests { get; } = new();
            public QueuedHandler(IEnumerable<HttpResponseMessage> responses) => _responses = new Queue<HttpResponseMessage>(responses);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add(request);
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private static HttpResponseMessage Ok(string body = """{"code":0}""") =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Unauthorized() =>
            new(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"code":401,"msg":"unauth"}""", Encoding.UTF8, "application/json") };

        private static HttpResponseMessage BodyAuthFailure() =>
            new(HttpStatusCode.OK) { Content = new StringContent("""{"code":1010003001,"msg":"expired"}""", Encoding.UTF8, "application/json") };

        [Fact]
        public async Task NoToken_DoesNotAttachBearer()
        {
            var state = new InlineTokenState();
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            var challenge = new Mock<IAuthChallengeService>();
            var inner = new QueuedHandler(new[] { Ok() });

            var handler = new AuthTokenHandler(state, store, refresher.Object, challenge.Object) { InnerHandler = inner };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://x/") };

            await client.GetAsync("/a");

            Assert.Single(inner.Requests);
            Assert.False(inner.Requests[0].Headers.Contains("Authorization"));
        }

        [Fact]
        public async Task WithToken_AttachesBearer()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("access-1", "r", null, 0));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            var challenge = new Mock<IAuthChallengeService>();
            var inner = new QueuedHandler(new[] { Ok() });

            var handler = new AuthTokenHandler(state, store, refresher.Object, challenge.Object) { InnerHandler = inner };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://x/") };

            await client.GetAsync("/a");

            Assert.Equal("Bearer access-1", string.Join(",", inner.Requests[0].Headers.GetValues("Authorization")));
        }

        [Fact]
        public async Task StatusCode401_TriggersRefreshAndRetries()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("old", "refresh-1", null, 0));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            refresher.Setup(x => x.RefreshAsync("refresh-1", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new AuthToken("new", "refresh-2", null, 0));
            var challenge = new Mock<IAuthChallengeService>();
            var inner = new QueuedHandler(new[] { Unauthorized(), Ok() });

            var handler = new AuthTokenHandler(state, store, refresher.Object, challenge.Object) { InnerHandler = inner };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://x/") };

            var res = await client.GetAsync("/a");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(2, inner.Requests.Count);
            Assert.Equal("Bearer new", string.Join(",", inner.Requests[1].Headers.GetValues("Authorization")));
            Assert.Equal("new", state.Current!.AccessToken);
            Assert.Equal("new", store.Saved!.AccessToken);
            challenge.Verify(x => x.ShowAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task BodyCode1010003001_TreatedAsUnauthorized()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("old", "refresh-1", null, 0));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            refresher.Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new AuthToken("new", "r2", null, 0));
            var challenge = new Mock<IAuthChallengeService>();
            var inner = new QueuedHandler(new[] { BodyAuthFailure(), Ok() });

            var handler = new AuthTokenHandler(state, store, refresher.Object, challenge.Object) { InnerHandler = inner };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://x/") };

            var res = await client.GetAsync("/a");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(2, inner.Requests.Count);
        }

        [Fact]
        public async Task RefreshFails_TriggersChallengeAndClearsToken()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("old", "refresh-1", null, 0));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            refresher.Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((AuthToken?)null);
            var challenge = new Mock<IAuthChallengeService>();
            challenge.Setup(x => x.ShowAsync(It.IsAny<CancellationToken>()))
                     .Callback(() => state.Set(new AuthToken("post-login", "r-new", null, 0)))
                     .ReturnsAsync(true);
            var inner = new QueuedHandler(new[] { Unauthorized(), Ok() });

            var handler = new AuthTokenHandler(state, store, refresher.Object, challenge.Object) { InnerHandler = inner };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://x/") };

            var res = await client.GetAsync("/a");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            challenge.Verify(x => x.ShowAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.True(store.Cleared);
            Assert.Equal("Bearer post-login", string.Join(",", inner.Requests[1].Headers.GetValues("Authorization")));
        }
    }
}
