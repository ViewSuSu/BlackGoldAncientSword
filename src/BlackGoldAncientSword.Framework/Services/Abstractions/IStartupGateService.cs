using System;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 启动期"未完成检测更新之前禁止一切 UI 操作"的门槛。
    /// <para>
    /// 与 <see cref="IUpdateGateService"/> 的区别：
    /// UpdateGate 只在**已确认有新版本**后阻塞在用户处理弹窗；
    /// 本 gate 覆盖更早的窗口——从 Shell 显示到 <c>CheckForUpdatesAsync</c> 返回之间，
    /// 期间 UI 应完全不可交互（登录按钮 / 侧边栏 / 关闭确认全部拦下）。
    /// </para>
    /// <para>
    /// 语义上是一个 latch：<see cref="Complete"/> 只允许被调用一次并把 <see cref="IsBusy"/> 从 true 翻到 false；
    /// 之后 <see cref="BusyChanged"/> 不再触发。
    /// </para>
    /// </summary>
    public interface IStartupGateService
    {
        /// <summary>true = 启动流程未完成（遮罩应显示，UI 应拦截点击）；false = 已完成。</summary>
        bool IsBusy { get; }

        /// <summary>
        /// <see cref="IsBusy"/> 变化通知。VM 订阅它把绑定属性同步到 UI。
        /// 只会触发一次（从 true → false）。
        /// </summary>
        event EventHandler? BusyChanged;

        /// <summary>
        /// 由 App 启动流程在更新检测完成后（无论成功 / 失败 / 异常）调用一次，
        /// 唤醒等待遮罩消失的 UI。重复调用是幂等的。
        /// </summary>
        void Complete();
    }
}
