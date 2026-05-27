using BlackGoldAncientSword.Modules.UI.Announcement.ViewModels;
using BlackGoldAncientSword.Modules.UI.Announcement.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class AnnouncementModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<AnnouncementPage, AnnouncementPageViewModel>();
        }
    }
}