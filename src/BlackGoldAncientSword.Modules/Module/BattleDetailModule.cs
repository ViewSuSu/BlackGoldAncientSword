using BlackGoldAncientSword.Modules.UI.BattleDetail.ViewModels;
using BlackGoldAncientSword.Modules.UI.BattleDetail.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class BattleDetailModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<BattleDetailPage, BattleDetailPageViewModel>();
        }
    }
}
