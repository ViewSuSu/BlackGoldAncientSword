using NarakaBladepoint.StatsAssistant.Modules.UI.Settings.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.Settings.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
{
    [Module(OnDemand = true)]
    public class SettingsModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<SettingsPage, SettingsPageViewModel>();
        }
    }
}
