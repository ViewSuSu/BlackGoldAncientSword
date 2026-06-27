using System.Windows;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 基于 WPF <see cref="Application"/> 的应用生命周期实现。这是 Framework 中唯一允许直接调用
    /// <c>Application.Current.MainWindow.Close()</c> / <c>Application.Current.Shutdown()</c> 的类。
    /// VM 应依赖 <see cref="IApplicationLifetime"/> 而非此具体实现。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class WpfApplicationLifetime : IApplicationLifetime
    {
        public void CloseMainWindow()
        {
            var app = Application.Current;
            app?.MainWindow?.Close();
        }

        public void Shutdown()
        {
            var app = Application.Current;
            app?.Shutdown();
        }

        public void MinimizeMainWindow()
        {
            var app = Application.Current;
            var window = app?.MainWindow;
            if (window is null) return;
            window.WindowState = WindowState.Minimized;
        }

        public void ForceTerminate()
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
