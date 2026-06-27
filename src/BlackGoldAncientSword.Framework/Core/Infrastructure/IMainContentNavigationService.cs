using System;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// MainContentRegion 的统一导航服务。线程契约：所有成员调用必须在 UI 线程上发生。
    /// </summary>
    public interface IMainContentNavigationService
    {
        /// <summary>
        /// 是否可执行 <see cref="GoBack"/>。等价于内部回退栈非空。
        /// 此属性不通过 INotifyPropertyChanged 通知，订阅方应在收到 <see cref="Navigated"/>
        /// 事件后主动重新读取最新值。
        /// </summary>
        bool CanGoBack { get; }

        /// <summary>
        /// 导航完成事件。**成功、失败、<see cref="Remove"/> 三种路径都会触发**：
        /// <list type="bullet">
        /// <item>导航成功：参数为目标 viewName。</item>
        /// <item>导航失败：参数为失败后**实际仍激活**的 View 名（通常是原页），
        /// 首次启动尚无激活页时为空串。订阅方可对比"自己请求的 viewName" 与本次回调参数识别失败。</item>
        /// <item><see cref="Remove"/> 后：参数为空串。</item>
        /// </list>
        /// 该事件同时表示 <see cref="CanGoBack"/> 可能已变化，订阅方可在此刷新相关绑定。
        /// <para>
        /// **重入约束**：订阅方不得在事件回调中同步调用本服务的
        /// <see cref="NavigateTo"/> / <see cref="GoBack"/> / <see cref="Remove"/>，
        /// 如需链式跳转请通过 <c>Dispatcher.BeginInvoke</c> 异步派发。
        /// </para>
        /// </summary>
        event Action<string>? Navigated;

        /// <summary>
        /// 导航到指定 View。若当前已激活同名 View 则跳过；
        /// 导航失败不会修改回退栈。
        /// </summary>
        void NavigateTo(string viewName, NavigationParameters? navigationParameters = null);

        /// <summary>
        /// 回退到最近一次成功导航前的 View；回退栈为空时回退到默认 StatsPage。
        /// </summary>
        void GoBack();

        /// <summary>
        /// 清空 region 内所有 View、清空回退栈、清空 Prism Journal，并使
        /// 此前所有未完成的导航回调失效。
        /// </summary>
        void Remove();
    }
}
