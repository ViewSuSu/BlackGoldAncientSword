using System.Reflection;
using NarakaBladepoint.StatsAssistant.Framework.Core.Extensions;

namespace NarakaBladepoint.StatsAssistant.GameMonitor;

/// <summary>
/// GameMonitor 模块自动注册。通过反射扫描程序集中带 [Component] 特性的类，
/// 自动按接口 -> 实现的映射注册到 DI 容器。
/// </summary>
public static class GameMonitorAutoRegister
{
    private static readonly Assembly _assembly;

    static GameMonitorAutoRegister()
    {
        _assembly = typeof(GameMonitorAutoRegister).Assembly;
    }

    /// <summary>
    /// 注册 GameMonitor 层的全部组件（由 App.xaml.cs 在 RegisterTypes 中调用）。
    /// </summary>
    public static IContainerRegistry RegisterGameMonitorLayer(this IContainerRegistry containerRegistry)
    {
        return containerRegistry.RegisterComponentsByAssembly(_assembly);
    }
}
