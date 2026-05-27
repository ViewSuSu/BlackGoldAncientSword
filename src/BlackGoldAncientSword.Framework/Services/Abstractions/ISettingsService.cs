namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface ISettingsService
    {
        AppSettings Current { get; }

        /// <summary>异步加载配置。</summary>
        Task LoadAsync();

        /// <summary>异步保存配置。</summary>
        Task SaveAsync();
    }
}
