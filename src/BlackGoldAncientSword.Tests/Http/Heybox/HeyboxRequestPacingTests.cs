using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    public class HeyboxRequestPacingTests
    {
        [Fact]
        public async Task WaitAsync_SpacesSuccessiveCallsByInterval()
        {
            const int interval = 300;

            await HeyboxRequestPacing.WaitAsync(interval, CancellationToken.None);

            var sw = Stopwatch.StartNew();
            await HeyboxRequestPacing.WaitAsync(interval, CancellationToken.None);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= interval * 0.85,
                $"两次调用只隔了 {sw.ElapsedMilliseconds}ms，远小于设定的 {interval}ms");
        }

        [Fact]
        public async Task WaitAsync_SerializesConcurrentCallers()
        {
            const int interval = 200;
            const int callers = 3;

            await HeyboxRequestPacing.WaitAsync(interval, CancellationToken.None);

            var sw = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, callers).Select(_ =>
                Task.Run(() => HeyboxRequestPacing.WaitAsync(interval, CancellationToken.None))));
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= interval * (callers - 1),
                $"{callers} 个并发调用总共只用了 {sw.ElapsedMilliseconds}ms，说明没有串行排队");
        }
    }
}
