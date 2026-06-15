using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 包装 Application.Current.Dispatcher，让 ViewModel 通过 IUIDispatcher 间接访问 UI 线程。
    /// 这是项目内唯一允许直接引用 System.Windows.Application / Dispatcher 的"特定 WPF 实现类"。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class WpfUIDispatcher : IUIDispatcher
    {
        private static Dispatcher? Dispatcher => Application.Current?.Dispatcher;

        public bool CheckAccess() => Dispatcher?.CheckAccess() ?? true;

        // 无 Application 时（关闭路径或测试宿主）退化为 inline 调用，等价于已在 UI 线程；
        // 用 Task.Run 会把后续 PropertyChanged 推到线程池线程，触发 WPF 绑定的跨线程异常。
        public Task InvokeAsync(Action action)
        {
            var d = Dispatcher;
            if (d == null) { action(); return Task.CompletedTask; }
            return d.InvokeAsync(action).Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            var d = Dispatcher;
            if (d == null) return Task.FromResult(func());
            return d.InvokeAsync(func).Task;
        }

        // Dispatcher.InvokeAsync 重载本身就接受 Func<Task> 并把它当作 DispatcherOperation<Task>，
        // 取 .Task 拿到的是"调度完成"的 Task，而非"内部 Task 完成"。.Unwrap() 才能拿到真正的 Task。
        public Task InvokeAsync(Func<Task> asyncAction)
        {
            if (asyncAction == null) throw new ArgumentNullException(nameof(asyncAction));
            var d = Dispatcher;
            if (d == null) return asyncAction();
            return d.InvokeAsync(asyncAction).Task.Unwrap();
        }

        public void BeginInvoke(Action action)
        {
            var d = Dispatcher;
            if (d == null) action();
            else d.BeginInvoke(action);
        }
    }
}
