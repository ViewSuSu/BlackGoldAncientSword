using System.Reflection;
using BlackGoldAncientSword.Framework.Core.Extensions;

namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// OCR 模块自动注册。通过反射扫描程序集中带 [Component] 特性的类，
/// 自动按接口 → 实现的映射注册到 DI 容器。
/// </summary>
public static class OcrAutoRegister
{
    private static readonly Assembly _assembly;

    static OcrAutoRegister()
    {
        _assembly = typeof(OcrAutoRegister).Assembly;
    }

    /// <summary>
    /// 注册 OCR 层的全部组件（由 App.xaml.cs 在 RegisterTypes 中调用）。
    /// </summary>
    public static IContainerRegistry RegisterOcrLayer(this IContainerRegistry containerRegistry)
    {
        return containerRegistry.RegisterComponentsByAssembly(_assembly);
    }
}
