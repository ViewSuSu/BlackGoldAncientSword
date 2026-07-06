using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Moq;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    public class AuthTokenExpiryMonitorTests
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

        private static AuthTokenExpiryMonitor Create(
            IAuthTokenState state,
            IAuthTokenStore store,
            IAuthTokenRefresher refresher,
            IAuthChallengeService challenge,
            long nowMs) =>
            new(state, store, refresher, challenge, () => nowMs, refreshLeadTimeMs: 30_000, checkInterval: TimeSpan.FromMinutes(10));

        [Fact]
        public async Task NoToken_DoesNothing()
        {
            var state = new InlineTokenState();
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>(MockBehavior.Strict);
            var challenge = new Mock<IAuthChallengeService>(MockBehavior.Strict);
            var monitor = Create(state, store, refresher.Object, challenge.Object, 1_000_000);
            await monitor.TickAsync();
            refresher.VerifyNoOtherCalls();
            challenge.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TokenFarFromExpiry_DoesNothing()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("a", "r", null, 5_000_000));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>(MockBehavior.Strict);
            var challenge = new Mock<IAuthChallengeService>(MockBehavior.Strict);
            var monitor = Create(state, store, refresher.Object, challenge.Object, 1_000_000);
            await monitor.TickAsync();
            refresher.VerifyNoOtherCalls();
            challenge.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TokenNearExpiry_RefreshesSilently()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("old", "r", null, 1_010_000));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            refresher.Setup(x => x.RefreshAsync("r", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new AuthToken("new", "r2", null, 9_999_999));
            var challenge = new Mock<IAuthChallengeService>(MockBehavior.Strict);

            // now=1_000_000, expiresAt=1_010_000 → 10s < 30s lead → refresh
            var monitor = Create(state, store, refresher.Object, challenge.Object, 1_000_000);
            await monitor.TickAsync();

            Assert.Equal("new", state.Current!.AccessToken);
            Assert.Equal("new", store.Saved!.AccessToken);
            challenge.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task RefreshFails_ClearsTokenAndShowsChallenge()
        {
            var state = new InlineTokenState();
            state.Set(new AuthToken("old", "r", null, 1_010_000));
            var store = new InlineTokenStore();
            var refresher = new Mock<IAuthTokenRefresher>();
            refresher.Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((AuthToken?)null);
            var challenge = new Mock<IAuthChallengeService>();
            challenge.Setup(x => x.ShowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var monitor = Create(state, store, refresher.Object, challenge.Object, 1_000_000);
            await monitor.TickAsync();

            Assert.Null(state.Current);
            Assert.True(store.Cleared);
            challenge.Verify(x => x.ShowAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
