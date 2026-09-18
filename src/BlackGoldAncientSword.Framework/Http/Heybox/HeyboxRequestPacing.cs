using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{
    public static class HeyboxRequestPacing
    {
        public const int DefaultIntervalMilliseconds = 1000;

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static long _lastTicks;

        public static Task WaitAsync(CancellationToken ct) => WaitAsync(DefaultIntervalMilliseconds, ct);

        public static async Task WaitAsync(int intervalMilliseconds, CancellationToken ct)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var last = Interlocked.Read(ref _lastTicks);
                if (last != 0)
                {
                    var remaining = intervalMilliseconds - (int)(Environment.TickCount64 - last);
                    if (remaining > 0) await Task.Delay(remaining, ct).ConfigureAwait(false);
                }
                Interlocked.Exchange(ref _lastTicks, Environment.TickCount64);
            }
            finally
            {
                Gate.Release();
            }
        }
    }

    public sealed class HeyboxRequestPacingHandler : DelegatingHandler
    {
        public int IntervalMilliseconds { get; init; } = HeyboxRequestPacing.DefaultIntervalMilliseconds;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await HeyboxRequestPacing.WaitAsync(IntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
