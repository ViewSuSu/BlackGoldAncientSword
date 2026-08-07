using BlackGoldAncientSword.Modules.UI.Sponsor.ViewModels;
using BlackGoldAncientSword.Modules.UI.Sponsor.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class SponsorModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SponsorPage, SponsorPageViewModel>();
        }
    }
}
