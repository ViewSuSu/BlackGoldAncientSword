using System.Windows;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 包装 System.Windows.Clipboard，让 ViewModel 通过 IClipboardService 间接访问剪贴板。
    /// 这是项目内唯一允许直接引用 System.Windows.Clipboard 的"特定 WPF 实现类"。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class WpfClipboardService : IClipboardService
    {
        public bool TrySetText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
