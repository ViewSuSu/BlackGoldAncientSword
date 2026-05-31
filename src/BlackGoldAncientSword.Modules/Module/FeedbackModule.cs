using BlackGoldAncientSword.Modules.UI.Feedback.ViewModels;
using BlackGoldAncientSword.Modules.UI.Feedback.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class FeedbackModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<FeedbackPage, FeedbackPageViewModel>();
        }
    }
}
