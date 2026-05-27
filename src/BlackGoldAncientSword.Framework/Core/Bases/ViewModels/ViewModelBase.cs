namespace BlackGoldAncientSword.Framework.Core.Bases.ViewModels
{
    public abstract class ViewModelBase : BindableBase, INavigationAware, IActiveAware
    {
        protected readonly IEventAggregator eventAggregator;
        protected readonly IRegionManager regionManager;
        protected readonly IContainerProvider containerProvider;

        public event EventHandler? IsActiveChanged;

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                IsActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected ViewModelBase()
        {
            containerProvider = PrismApplicationBase.ContainerProvider;
            eventAggregator = containerProvider.Resolve<IEventAggregator>();
            regionManager = containerProvider.Resolve<IRegionManager>();
        }

        protected virtual bool IsNavigationTargetExecute(NavigationContext navigationContext) => true;

        public bool IsNavigationTarget(NavigationContext navigationContext) => IsNavigationTargetExecute(navigationContext);

        protected virtual void OnNavigatedFromExecute(NavigationContext navigationContext) { }

        public void OnNavigatedFrom(NavigationContext navigationContext) => OnNavigatedFromExecute(navigationContext);

        protected virtual void OnNavigatedToExecute(NavigationContext navigationContext) { }

        public void OnNavigatedTo(NavigationContext navigationContext) => OnNavigatedToExecute(navigationContext);
    }
}
