using NarakaBladepoint.StatsAssistant.Modules.UI.Announcement.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.Announcement.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
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