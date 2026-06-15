using BlackGoldAncientSword.Modules.UI.Stats.Services;
using BlackGoldAncientSword.Modules.UI.Stats.ViewModels;
using BlackGoldAncientSword.Modules.UI.Stats.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class StatsModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // [Component] 已经被 ModuleCatalogConfigManager.RegisterModuleLayer() 自动扫描注册为 Singleton；
            // 这里再显式注册一次，让模块依赖图在 RegisterTypes 中一目了然，便于排查与文档。
            containerRegistry.RegisterSingleton<PlayerStatsLoader>();
            containerRegistry.RegisterSingleton<BattleListLoader>();
            containerRegistry.RegisterForNavigation<StatsPage, StatsPageViewModel>();
        }
    }
}
