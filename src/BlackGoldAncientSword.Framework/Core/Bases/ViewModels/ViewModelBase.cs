using System;

namespace BlackGoldAncientSword.Framework.Core.Bases.ViewModels
{
    /// <summary>
    /// 项目所有 ViewModel 的基类。
    /// <para>
    /// 属性变更通知规约（与项目硬性规约一致）：
    /// <list type="bullet">
    ///   <item>禁止使用 BindableBase 自带的 <c>SetProperty</c>；</item>
    ///   <item>统一在 setter 中调用 <c>RaisePropertyChanged</c>（由编译器通过 <c>[CallerMemberName]</c> 自动填充属性名）；</item>
    ///   <item>禁止硬编码属性名字符串字面量，需显式传参时使用 <c>nameof</c>。</item>
    /// </list>
    /// </para>
    /// </summary>
    public abstract class ViewModelBase : BindableBase, INavigationAware, IActiveAware, IDisposable
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

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing) { }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
