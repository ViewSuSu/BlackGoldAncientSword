using NarakaBladepoint.StatsAssistant.Framework.Core.Consts;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Bases.ViewModels
{
    public abstract class CanRemoveHomePageRegionViewModelBase : ViewModelBase
    {
        private DelegateCommand? _returnCommand;
        public DelegateCommand ReturnCommand =>
            _returnCommand ??= new DelegateCommand(() =>
            {
                eventAggregator.GetEvent<RemoveHomePageRegionEvent>().Publish();
            });

        private DelegateCommand? _removeAllHomePageCommand;
        public DelegateCommand RemoveAllHomePageCommand =>
            _removeAllHomePageCommand ??= new DelegateCommand(() =>
            {
                eventAggregator.GetEvent<RemoveAllHomePageRegionEvent>().Publish();
            });
    }
}
