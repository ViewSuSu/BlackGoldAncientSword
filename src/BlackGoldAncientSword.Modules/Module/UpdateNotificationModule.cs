using BlackGoldAncientSword.Modules.UI.UpdateNotification.ViewModels;
using BlackGoldAncientSword.Modules.UI.UpdateNotification.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class UpdateNotificationModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<UpdateNotificationPage, UpdateNotificationPageViewModel>();
        }
    }
}
