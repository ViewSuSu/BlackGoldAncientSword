namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>剪贴板抽象，避免 ViewModel 直接引用 System.Windows.Clipboard。</summary>
    public interface IClipboardService
    {
        /// <summary>写入纯文本到剪贴板；失败不抛异常，调用方可忽略返回值。</summary>
        bool TrySetText(string text);
    }
}
