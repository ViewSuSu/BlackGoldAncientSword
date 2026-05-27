using BlackGoldAncientSword.Modules.UI.Search.ViewModels;
using BlackGoldAncientSword.Modules.UI.Search.Views;

namespace BlackGoldAncientSword.Modules.Module
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
