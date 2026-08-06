using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// 尾沿防抖：短时间内多次调用 <see cref="Trigger"/> 只会在最后一次触发之后延迟
    /// <see cref="_delayMs"/> ms 执行一次回调。用于把"连续变更的多个筛选条件"合并成一次动作，
    /// 避免每改一个属性就整队重发一批相同参数的请求。
    /// <para>线程安全，可从任意线程 <see cref="Trigger"/>；回调在 ThreadPool 线程上执行，调用方自行 marshal。</para>
    /// </summary>
    public sealed class TrailingDebouncer : IDisposable
    {
        private readonly int _delayMs;
        private readonly Func<CancellationToken, Task> _action;
        private readonly object _gate = new();
        private CancellationTokenSource? _cts;

        public TrailingDebouncer(int delayMs, Func<CancellationToken, Task> action)
        {
            if (delayMs < 0)
                throw new ArgumentOutOfRangeException(nameof(delayMs));
            _delayMs = delayMs;
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        /// <summary>登记一次触发：取消上一次尚未到期的等待，重新计时；到期后执行回调一次。</summary>
        public void Trigger()
        {
            CancellationToken ct;
            lock (_gate)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                ct = _cts.Token;
            }
            _ = RunAsync(ct);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(_delayMs, ct).ConfigureAwait(false);
                await _action(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 被下一次 Trigger 取代——正常路径。
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(TrailingDebouncer)}.{nameof(RunAsync)}");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}
