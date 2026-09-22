using BlackGoldAncientSword.Modules.UI.BindRole.ViewModels;
using BlackGoldAncientSword.Modules.UI.BindRole.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class BindRoleModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<BindRolePage, BindRolePageViewModel>();
        }
    }
}
