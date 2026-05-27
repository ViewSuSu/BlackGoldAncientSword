using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;
using NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure;
using NarakaBladepoint.StatsAssistant.Modules.UI.Home.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.Home.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
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
