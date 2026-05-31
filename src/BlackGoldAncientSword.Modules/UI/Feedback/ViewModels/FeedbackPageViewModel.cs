using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.Feedback.ViewModels
{
    public class FeedbackPageViewModel : ViewModelBase
    {
        private readonly IRegionManager _regionManager;

        public FeedbackPageViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                var region = _regionManager.Regions[GlobalConstant.FeedbackRegion];
                region.RemoveAll();
            });

        private DelegateCommand? _confirmCommand;
        public DelegateCommand ConfirmCommand =>
            _confirmCommand ??= new DelegateCommand(() =>
            {
                var region = _regionManager.Regions[GlobalConstant.FeedbackRegion];
                region.RemoveAll();
            });
    }
}
