using NarakaBladepoint.StatsAssistant.Modules.UI.Home.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.Home.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
{
    [Module(OnDemand = true)]
    public class HomeModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<HomePage, HomePageViewModel>();
        }
    }
}