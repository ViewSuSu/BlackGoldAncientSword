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
            containerRegistry.RegisterForNavigation<StatsPage, StatsPageViewModel>();
        }
    }
}
