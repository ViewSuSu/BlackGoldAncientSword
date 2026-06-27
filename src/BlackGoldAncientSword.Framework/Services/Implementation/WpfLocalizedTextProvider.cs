using System.Windows;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 通过 <c>Application.Current.TryFindResource</c> 解析资源键。
    /// 这是 WPF 实现专属：唯一允许直接引用 <c>System.Windows.Application</c> 的资源解析类。
    /// VM 应该依赖 <see cref="ILocalizedTextProvider"/> 而非此具体实现。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class WpfLocalizedTextProvider : ILocalizedTextProvider
    {
        public string Get(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            var app = Application.Current;
            if (app == null) return fallback;
            return app.TryFindResource(key) as string ?? fallback;
        }
    }
}
