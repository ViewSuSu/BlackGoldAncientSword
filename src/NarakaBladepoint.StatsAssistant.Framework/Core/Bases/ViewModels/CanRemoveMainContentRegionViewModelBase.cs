using NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Bases.ViewModels
{
    public abstract class CanRemoveMainContentRegionViewModelBase : ViewModelBase
    {
        private readonly IMainContentNavigationService _navigation;

        protected CanRemoveMainContentRegionViewModelBase(IMainContentNavigationService navigation)
        {
            _navigation = navigation;
        }

        private DelegateCommand? _returnCommand;
        public DelegateCommand ReturnCommand =>
            _returnCommand ??= new DelegateCommand(() =>
            {
                _navigation.Remove();
            });
    }
}
