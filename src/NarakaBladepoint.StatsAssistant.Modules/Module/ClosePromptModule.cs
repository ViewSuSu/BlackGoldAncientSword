using NarakaBladepoint.StatsAssistant.Modules.UI.ClosePrompt.ViewModels;
using NarakaBladepoint.StatsAssistant.Modules.UI.ClosePrompt.Views;

namespace NarakaBladepoint.StatsAssistant.Modules.Module
{
    [Module(OnDemand = true)]
    public class ClosePromptModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<ClosePromptPage, ClosePromptPageViewModel>();
        }
    }
}