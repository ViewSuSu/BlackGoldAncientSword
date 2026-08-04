using System.Diagnostics;
using System.Text;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal
{
    /// <summary>
    /// 日志文件读取器。承担：
    /// 1) 通过 <see cref="SemaphoreSlim"/> 串行化 FSW 回调与 Poll 循环对同一文件的并发读取；
    /// 2) UTF-8 编码下的字节范围读取（含锁文件 IOException 重试）；
    /// 3) 启动期一次性读取整文件用于回放历史日志。
    /// 故意不持有 _lastPosition 等"位置"状态——位置状态归 <see cref="BattleStateMachine"/>，
    /// 因为它要在 stateLock 内与战斗状态一同 reset。
    /// </summary>
    internal sealed class LogReader : IDisposable
    {
        private readonly SemaphoreSlim _readSemaphore = new(1, 1);

        /// <summary>
        /// 尝试以 WaitAsync(0) 抢占信号量并执行读取动作。返回是否实际执行。
        /// 用于 Poll 循环 / FSW 回调防止并发读取同一文件造成位置错乱。
        /// 内部已吞 <see cref="ObjectDisposedException"/>（Stop 后残余回调到达），
        /// 不会向 caller 抛 ODE。
        /// </summary>
        public async Task<bool> TryReadWithLockAsync(Func<Task> readAction, CancellationToken token)
        {
            bool entered = false;
            try
            {
                entered = await _readSemaphore.WaitAsync(0, token).ConfigureAwait(false);
                if (!entered) return false;
                await readAction().ConfigureAwait(false);
                return true;
            }
            catch (ObjectDisposedException)
            {
                // Dispose 之后到达的残余回调；正常路径，返回 false。
                return false;
            }
            finally
            {
                if (entered)
                {
                    try { _readSemaphore.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        /// <summary>
        /// 启动期完整读取日志文件内容（UTF-8）。文件被游戏进程独占写入，
        /// 故必须使用 <see cref="FileShare.ReadWrite"/>。
        /// </summary>
        public async Task<(string content, long length)> ReadAllAsync(string fullPath)
        {
            await using var fs = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);
            using var reader = new StreamReader(fs);
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);

            long length = 0;
            try { length = new FileInfo(fullPath).Length; }
            catch (Exception ex)
            {
                // FileInfo.Length 失败：罕见的并发删除/重命名场景，下次 poll 会重新拉取。
                AppLog.Error(ex, nameof(LogReader), "FileInfo.Length read failed");
            }
            return (content, length);
        }

        /// <summary>
        /// 读取文件 [startPos, endPos) 字节范围。带最多 3 次 IOException 重试（间隔 50ms），
        /// 应对游戏进程持有文件锁的瞬时窗口。失败返回 null。
        /// </summary>
        public static async Task<byte[]?> ReadFileRangeAsync(string fullPath, long startPos, long endPos)
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    await using var fs = new FileStream(
                        fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                        bufferSize: 4096, useAsync: true);
                    fs.Seek(startPos, SeekOrigin.Begin);

                    int bytesToRead = (int)(endPos - startPos);
                    var buffer = new byte[bytesToRead];
                    await fs.ReadExactlyAsync(buffer, 0, bytesToRead).ConfigureAwait(false);
                    return buffer;
                }
                catch (IOException)
                {
                    if (retry == 2) return null;
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
            return null;
        }

        /// <summary>
        /// 把字节数组按"最后一个 \n"截断，并以 UTF-8 解码出文本。
        /// 返回 false 表示无完整行（不应推进位置）。
        /// 输出 <paramref name="text"/> 仅包含到末尾 \n（含）的完整行；
        /// <paramref name="consumedBytes"/> 为已消费的字节数（= 最后一个 \n 索引 + 1），
        /// 调用方据此推进 LastPosition。
        /// </summary>
        internal static bool TruncateToLastNewline(byte[] data, out string text, out int consumedBytes)
        {
            text = string.Empty;
            consumedBytes = 0;
            if (data == null || data.Length == 0) return false;

            int lastNewline = -1;
            for (int i = data.Length - 1; i >= 0; i--)
            {
                if (data[i] == (byte)'\n')
                {
                    lastNewline = i;
                    break;
                }
            }

            if (lastNewline < 0) return false;

            consumedBytes = lastNewline + 1;
            text = Encoding.UTF8.GetString(data, 0, consumedBytes);
            return true;
        }

        /// <summary>
        /// 获取当前文件长度。失败返回 null（调用方据此跳过本轮 poll）。
        /// </summary>
        public static long? TryGetFileLength(string fullPath)
        {
            try { return new FileInfo(fullPath).Length; }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(LogReader), "TryGetFileLength failed");
                return null;
            }
        }

        public void Dispose()
        {
            // 故意不在 Stop 阶段 Dispose——FSW 解绑后仍可能有 ThreadPool 队列里的回调命中。
            // 由 facade 在自己 Dispose 末尾调用本方法，并依赖各处 catch ObjectDisposedException 兜底。
            _readSemaphore.Dispose();
        }
    }
}
