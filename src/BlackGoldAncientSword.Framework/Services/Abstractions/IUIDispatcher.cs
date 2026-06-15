using System;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>UI 线程调度抽象，让 ViewModel 无需直接引用 WPF 即可 marshal 到 UI 线程。</summary>
    public interface IUIDispatcher
    {
        /// <summary>当前线程是否为 UI 线程。</summary>
        bool CheckAccess();

        /// <summary>异步在 UI 线程调度委托执行，返回可 await 的 Task。</summary>
        Task InvokeAsync(Action action);

        /// <summary>异步在 UI 线程调度委托执行并返回结果。</summary>
        Task<T> InvokeAsync<T>(Func<T> func);

        /// <summary>
        /// 异步在 UI 线程调度**异步**委托执行，返回的 Task 在 <paramref name="asyncAction"/> 返回的 Task
        /// 完成时一并完成。用于避免把 async lambda 误传给 <see cref="InvokeAsync(Action)"/> 退化为 async void。
        /// </summary>
        Task InvokeAsync(Func<Task> asyncAction);

        /// <summary>fire-and-forget 在 UI 线程异步执行（用于 Dispatcher 关闭路径，避免死锁）。</summary>
        void BeginInvoke(Action action);
    }
}
