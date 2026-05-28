using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class TeamInfoModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<TeamInfoPage, TeamInfoPageViewModel>();
        }
    }
}
