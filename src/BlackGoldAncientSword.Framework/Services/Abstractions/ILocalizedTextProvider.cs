namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 本地化字符串资源解析抽象。VM 通过此接口拿到 ResourceDictionary 中的字符串键值，
    /// 避免直接耦合 <c>System.Windows.Application.Current.TryFindResource</c>。
    /// </summary>
    public interface ILocalizedTextProvider
    {
        /// <summary>
        /// 解析资源键对应的字符串；若键不存在或解析结果非字符串，返回 <paramref name="fallback"/>。
        /// 不抛异常，调用方可直接把返回值用于 UI 绑定或日志。
        /// </summary>
        string Get(string key, string fallback);
    }
}
