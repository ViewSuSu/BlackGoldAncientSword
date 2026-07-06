using BlackGoldAncientSword.Modules.UI.AuthChallenge.ViewModels;
using BlackGoldAncientSword.Modules.UI.AuthChallenge.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = true)]
    public class AuthChallengeModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider) { }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<AuthChallengePage, AuthChallengePageViewModel>();
        }
    }
}
