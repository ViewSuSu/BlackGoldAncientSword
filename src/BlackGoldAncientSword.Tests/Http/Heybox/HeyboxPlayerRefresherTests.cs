using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    public class HeyboxPlayerRefresherTests
    {
        private static Func<CancellationToken, Task<string?>> States(params string?[] states)
        {
            var queue = new Queue<string?>(states);
            return _ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : null);
        }

        [Fact]
        public async Task RefreshAsync_ReturnsTrueImmediatelyWhenCompleted()
        {
            var calls = 0;
            var refresher = new HeyboxPlayerRefresher();

            var result = await refresher.RefreshAsync(
                "role-completed",
                _ => { calls++; return Task.FromResult<string?>("ok"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.True(result);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task RefreshAsync_RetriesWhilePendingThenSucceeds()
        {
            var refresher = new HeyboxPlayerRefresher();

            var result = await refresher.RefreshAsync(
                "role-pending-then-ok",
                States("waiting", "updating", "ok"),
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.True(result);
        }

        [Fact]
        public async Task RefreshAsync_StopsAfterAttemptCeiling()
        {
            var calls = 0;
            var refresher = new HeyboxPlayerRefresher();

            var result = await refresher.RefreshAsync(
                "role-always-pending",
                _ => { calls++; return Task.FromResult<string?>("updating"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(result);
            Assert.Equal(3, calls);
        }

        [Fact]
        public async Task RefreshAsync_StopsOnTerminalState()
        {
            var calls = 0;
            var refresher = new HeyboxPlayerRefresher();

            var result = await refresher.RefreshAsync(
                "role-failed",
                _ => { calls++; return Task.FromResult<string?>("failed"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(result);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task RefreshAsync_StopsOnMissingState()
        {
            var calls = 0;
            var refresher = new HeyboxPlayerRefresher();

            var result = await refresher.RefreshAsync(
                "role-no-state",
                _ => { calls++; return Task.FromResult<string?>(null); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(result);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task RefreshAsync_ThrottlesSameRoleWithinWindow()
        {
            var calls = 0;
            var refresher = new HeyboxPlayerRefresher();

            Func<CancellationToken, Task<string?>> request =
                _ => { calls++; return Task.FromResult<string?>("ok"); };

            var first = await refresher.RefreshAsync(
                "role-throttled", request, CancellationToken.None, TimeSpan.Zero);

            var second = await refresher.RefreshAsync(
                "role-throttled", request, CancellationToken.None, TimeSpan.Zero);

            Assert.True(first);
            Assert.False(second);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task RefreshAsync_ThrottlesPerRole()
        {
            var refresher = new HeyboxPlayerRefresher();

            var first = await refresher.RefreshAsync(
                "role-a", States("ok"), CancellationToken.None, TimeSpan.Zero);

            var second = await refresher.RefreshAsync(
                "role-b", States("ok"), CancellationToken.None, TimeSpan.Zero);

            Assert.True(first);
            Assert.True(second);
        }

        [Fact]
        public async Task RefreshAsync_SkipsBlankRoleId()
        {
            var calls = 0;
            var refresher = new HeyboxPlayerRefresher();

            var result = await refresher.RefreshAsync(
                "   ",
                _ => { calls++; return Task.FromResult<string?>("ok"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(result);
            Assert.Equal(0, calls);
        }

        [Fact]
        public async Task RefreshAsync_PropagatesCallerCancellation()
        {
            var refresher = new HeyboxPlayerRefresher();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                refresher.RefreshAsync(
                    "role-cancelled",
                    States("waiting"),
                    cts.Token,
                    TimeSpan.Zero));
        }

        [Theory]
        [InlineData("ok", false, true)]
        [InlineData("OK", false, true)]
        [InlineData("waiting", true, false)]
        [InlineData("updating", true, false)]
        [InlineData("failed", false, false)]
        [InlineData("", false, false)]
        [InlineData(null, false, false)]
        public void StateClassification(string? state, bool pending, bool completed)
        {
            Assert.Equal(pending, HeyboxPlayerRefresher.IsPending(state));
            Assert.Equal(completed, HeyboxPlayerRefresher.IsCompleted(state));
        }
    }
}
