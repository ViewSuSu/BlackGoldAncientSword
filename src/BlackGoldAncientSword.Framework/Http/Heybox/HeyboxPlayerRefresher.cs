using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxPlayerRefresher
    {
        private const string StateOk = "ok";

        private const string StateWaiting = "waiting";

        private const string StateUpdating = "updating";

        private const int MaxAttempts = 3;

        private const int RequestTimeoutMilliseconds = 8000;

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

        private static readonly TimeSpan AttemptWindow = TimeSpan.FromSeconds(60);

        private readonly object _sync = new();

        private readonly Dictionary<string, DateTimeOffset> _attempts = new(StringComparer.Ordinal);

        public Task<bool> RefreshAsync(string roleId, string server, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(server)) return Task.FromResult(false);

            return RefreshAsync(roleId, token => RequestUpdateAsync(roleId, server, token), ct);
        }

        internal async Task<bool> RefreshAsync(
            string roleId,
            Func<CancellationToken, Task<string?>> requestState,
            CancellationToken ct,
            TimeSpan? retryDelay = null)
        {
            if (string.IsNullOrWhiteSpace(roleId)) return false;
            if (!TryBeginAttempt(roleId)) return false;

            var delay = retryDelay ?? RetryDelay;

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var state = await requestState(ct).ConfigureAwait(false);
                if (IsCompleted(state)) return true;
                if (!IsPending(state)) return false;

                if (attempt < MaxAttempts - 1)
                    await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            return false;
        }

        public static bool IsPending(string? state)
            => string.Equals(state, StateWaiting, StringComparison.OrdinalIgnoreCase)
               || string.Equals(state, StateUpdating, StringComparison.OrdinalIgnoreCase);

        public static bool IsCompleted(string? state)
            => string.Equals(state, StateOk, StringComparison.OrdinalIgnoreCase);

        private static async Task<string?> RequestUpdateAsync(
            string roleId, string server, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(RequestTimeoutMilliseconds);

            try
            {
                var response = await NarakaApiClient.UpdatePlayerAsync(
                    server: server,
                    roleId: roleId,
                    ct: linked.Token).ConfigureAwait(false);

                return response?.Result?.State;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool TryBeginAttempt(string roleId)
        {
            var now = DateTimeOffset.UtcNow;

            lock (_sync)
            {
                DropExpiredUnlocked(now);

                if (_attempts.TryGetValue(roleId, out var last) && now - last < AttemptWindow)
                    return false;

                _attempts[roleId] = now;
                return true;
            }
        }

        private void DropExpiredUnlocked(DateTimeOffset now)
        {
            var expired = _attempts
                .Where(kv => now - kv.Value >= AttemptWindow)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expired) _attempts.Remove(key);
        }
    }
}
