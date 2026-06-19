namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 应用进程生命周期抽象：让 VM 通过此接口请求关闭主窗口或退出整个进程，
    /// 避免 VM 直接依赖 <c>System.Windows.Application.Current</c>。
    /// </summary>
    public interface IApplicationLifetime
    {
        /// <summary>关闭当前主窗口（窗口关闭事件会按 WPF 默认行为处理后续生命周期）。</summary>
        void CloseMainWindow();

        /// <summary>立即终止应用进程（等价于 <c>Application.Current.Shutdown()</c>）。</summary>
        void Shutdown();
    }
}
