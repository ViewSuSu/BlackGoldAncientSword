using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = false)]
    public class TeamInfoModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // [Component] 已经被 ModuleCatalogConfigManager.RegisterModuleLayer() 自动扫描注册为 Singleton；
            // 这里再显式注册一次，让模块依赖图在 RegisterTypes 中一目了然，便于排查与文档。
            containerRegistry.RegisterSingleton<TeamOcrCoordinator>();
            containerRegistry.RegisterSingleton<TeamMemberLoader>();
            containerRegistry.RegisterSingleton<PlayerStatsLoader>();
            containerRegistry.RegisterSingleton<TeamInfoPageViewModel>();
            containerRegistry.RegisterForNavigation<TeamInfoPage, TeamInfoPageViewModel>();
        }
    }
}
