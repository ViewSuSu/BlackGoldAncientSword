using BlackGoldAncientSword.Modules.UI.ClosePrompt.ViewModels;
using BlackGoldAncientSword.Modules.UI.ClosePrompt.Views;

namespace BlackGoldAncientSword.Modules.Module
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