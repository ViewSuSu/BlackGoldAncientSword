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
    }
}
