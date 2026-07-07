using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.App.Services
{
    /// <summary>
    /// 更新弹窗门槛的具体实现：<see cref="WaitAsync"/> 返回 TCS，
    /// <see cref="Complete"/> 唤醒所有 await 者。
    /// 处理竞态：Complete 可能因 UI Dispatcher 队列先执行而早于 WaitAsync 到达，
    /// 用 <see cref="_completed"/> latch 记住"已完成"，后续 WaitAsync 直接返回完成的 Task。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class UpdateGateService : IUpdateGateService
    {
        private readonly object _sync = new();
        private TaskCompletionSource<bool>? _current;
        private bool _completed;

        public Task WaitAsync(CancellationToken ct = default)
        {
            TaskCompletionSource<bool> tcs;
            lock (_sync)
            {
                if (_completed) return Task.CompletedTask;
                _current ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _current;
            }

            if (ct.CanBeCanceled)
                ct.Register(Complete);

            return tcs.Task;
        }

        public void Complete()
        {
            TaskCompletionSource<bool>? tcs;
            lock (_sync)
            {
                _completed = true;
                tcs = _current;
                _current = null;
            }
            tcs?.TrySetResult(true);
        }
    }
}
