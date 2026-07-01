namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface ISettingsService
    {
        /// <summary>强制重新从 settings.json 加载配置，不做缓存，覆盖 Current。</summary>
        Task ReloadAsync();

        AppSettings Current { get; }

        /// <summary>异步加载配置。</summary>
        Task LoadAsync();

        /// <summary>异步保存配置。</summary>
        Task SaveAsync();

        /// <summary>
        /// 配置发生变更后触发。触发来源两类：
        /// <list type="bullet">
        ///   <item>本进程内 <see cref="SaveAsync"/> 完成后主动广播；</item>
        ///   <item>后台 FileSystemWatcher 检测到 settings.json 被外部改写后，Reload 并广播。</item>
        /// </list>
        /// 订阅方需自行 marshal 到 UI 线程。事件可能在后台线程回调，处理逻辑必须线程安全。
        /// </summary>
        event EventHandler? SettingsChanged;
    }
}
