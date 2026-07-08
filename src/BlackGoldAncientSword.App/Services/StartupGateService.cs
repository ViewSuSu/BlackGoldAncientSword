using System;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.App.Services
{
    /// <summary>
    /// 启动闸门的具体实现：初始 <see cref="IsBusy"/>=true，<see cref="Complete"/> 第一次调用把它翻到 false 并派发
    /// <see cref="BusyChanged"/>。重复 Complete 幂等；多线程调用用简单 lock 保护，避免多次触发事件。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class StartupGateService : IStartupGateService
    {
        private readonly object _sync = new();
        private bool _isBusy = true;

        public bool IsBusy
        {
            get { lock (_sync) return _isBusy; }
        }

        public event EventHandler? BusyChanged;

        public void Complete()
        {
            lock (_sync)
            {
                if (!_isBusy) return;
                _isBusy = false;
            }
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
