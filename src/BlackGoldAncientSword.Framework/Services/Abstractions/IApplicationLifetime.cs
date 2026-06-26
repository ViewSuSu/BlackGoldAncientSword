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

        /// <summary>
        /// 把主窗口最小化到任务栏（<c>WindowState = Minimized</c>），不触发 <c>Closing</c> 事件。
        /// 用于"关闭确认"等需要绕过窗口关闭流水线的场景，避免与 OnClosing 中的关闭策略形成重入。
        /// </summary>
        void MinimizeMainWindow();

        /// <summary>
        /// 强制终止当前进程（<c>Process.Kill</c>），不执行任何优雅清理。
        /// 依赖 JobObject (<c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>) 由 OS 自动清理子进程。
        /// 用于"直接退出"路径，绕过 WPF 的 <c>Application.Shutdown</c> 以及由其触发的 <c>OnClosing</c>。
        /// </summary>
        void ForceTerminate();
    }
}
