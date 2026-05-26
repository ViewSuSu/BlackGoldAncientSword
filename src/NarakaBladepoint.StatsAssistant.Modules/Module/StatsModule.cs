using NarakaBladepoint.StatsAssistant.Modules.UI.Stats.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.Stats.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
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
