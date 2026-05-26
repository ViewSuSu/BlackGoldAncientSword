using NarakaBladepoint.StatsAssistant.Modules.UI.Search.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.Search.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
{
    [Module(OnDemand = true)]
    public class SearchModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SearchPage, SearchPageViewModel>();
        }
    }
}
