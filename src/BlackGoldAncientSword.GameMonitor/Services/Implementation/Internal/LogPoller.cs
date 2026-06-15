using System.Diagnostics;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal
{
    /// <summary>
    /// 兜底 Poll 循环。FileSystemWatcher 在某些场景下漏触发（NTFS 缓冲合并写、
    /// 网络盘、远程会话等），需用固定间隔 Poll 兜底。
    /// 本类只负责"定时驱动 + 取消语义 + 异常吞咽"，不持有 IO/状态。
    /// </summary>
    internal sealed class LogPoller
    {
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

        private readonly TimeSpan _interval;

        public LogPoller() : this(DefaultPollInterval) { }

        public LogPoller(TimeSpan interval)
        {
            _interval = interval;
        }

        /// <summary>
        /// 启动 Poll 循环：周期性调用 <paramref name="readAction"/>，直到 token 被取消。
        /// <para>异常处理：</para>
        /// <list type="bullet">
        ///   <item>OCE / ODE 直接退出循环（Stop 后清理路径）；</item>
        ///   <item>其余异常吞掉并 <see cref="Debug.WriteLine"/>，避免 Poll 提前结束让监控失效。</item>
        /// </list>
        /// </summary>
        public async Task RunAsync(Func<CancellationToken, Task> readAction, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await readAction(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Stop 之后 semaphore 等资源被 Dispose；直接退出循环。
                    break;
                }
                catch (Exception ex)
                {
                    // 兜底：吞下非 OCE/ODE 的异常（IO 异常 / 行解析异常等），避免 PollLoop 提前结束让监控失效。
                    // 但必须留诊断线索——监控失灵但无日志会导致用户报"战绩不更新"无从排查。
                    Debug.WriteLine($"[{nameof(LogPoller)}] PollLoop iteration failed: {ex.Message}");
                }
            }
        }
    }
}
