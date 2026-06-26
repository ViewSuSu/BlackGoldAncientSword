using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.App
{
    /// <summary>
    /// App 程序集标记实现，仅用于让 Framework 拿到 App 程序集引用。
    /// 必须位于 App 项目内，typeof(this).Assembly 即指向 BlackGoldAncientSword.App.dll。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    internal sealed class AppAssemblyMarker : IAppAssemblyMarker
    {
    }
}
