namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// App 程序集标记接口。
    /// Why: Framework 程序集无法反向引用 App 程序集，但 UpdateService 等组件需要读取 App 程序集的 AssemblyInformationalVersion。
    /// How: App 项目实现并注册此接口，Framework 通过 DI 注入实现，再用 typeof(impl).Assembly 反推 App 程序集。
    /// </summary>
    public interface IAppAssemblyMarker
    {
    }
}
