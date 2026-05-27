using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Modules.UI.Home.ViewModels;
using BlackGoldAncientSword.Modules.UI.Home.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand =true)]
    public class HomeModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            var navigation = containerProvider.Resolve<IMainContentNavigationService>();
            navigation.NavigateTo(PageNames.HomePage);
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<HomePage, HomePageViewModel>();
        }
    }
}
