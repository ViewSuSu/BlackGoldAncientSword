using BlackGoldAncientSword.Modules.UI.UpdateLog.ViewModels;
using BlackGoldAncientSword.Modules.UI.UpdateLog.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class UpdateLogModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<UpdateLogPage, UpdateLogPageViewModel>();
        }
    }
}
