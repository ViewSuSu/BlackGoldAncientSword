using System;
using System.Threading;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// 搜索防抖闸门：两次成功触发之间必须至少间隔 <see cref="IntervalMilliseconds"/> ms；
    /// 过快调用 <see cref="TryEnter"/> 会返回 false，交由调用方向用户反馈"点击过快"。
    /// 线程安全，可跨 UI 线程与后台任务共享同一实例。
    /// </summary>
    public sealed class SearchDebounceGate
    {
        public const int DefaultIntervalMilliseconds = 1000;

        public int IntervalMilliseconds { get; }

        private long _lastFireTicks;

        public SearchDebounceGate(int intervalMilliseconds = DefaultIntervalMilliseconds)
        {
            if (intervalMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
            IntervalMilliseconds = intervalMilliseconds;
        }

        /// <summary>
        /// 尝试进入一次搜索：距上次成功触发未超过 <see cref="IntervalMilliseconds"/> ms 时返回 false 且不刷新时间戳，
        /// 否则更新时间戳并返回 true。
        /// </summary>
        public bool TryEnter()
        {
            var now = Environment.TickCount64;
            while (true)
            {
                var last = Interlocked.Read(ref _lastFireTicks);
                if (last != 0 && now - last < IntervalMilliseconds)
                    return false;
                if (Interlocked.CompareExchange(ref _lastFireTicks, now, last) == last)
                    return true;
            }
        }
    }
}
