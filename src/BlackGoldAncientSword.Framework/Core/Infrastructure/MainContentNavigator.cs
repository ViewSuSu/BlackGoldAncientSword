using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    [Component(ComponentLifetime.Singleton)]
    public class MainContentNavigator : IMainContentNavigationService
    {
        public event EventHandler? Removed;
        public event Action<string>? Navigated;

        private readonly IRegionManager _regionManager;
        private readonly IModuleManager _moduleManager;
        private readonly Stack<string> _history = new();

        public bool CanGoBack => _history.Count > 0;

        public MainContentNavigator(IRegionManager regionManager, IModuleManager moduleManager)
        {
            _regionManager = regionManager;
            _moduleManager = moduleManager;
        }

        public bool HasActiveContent
        {
            get
            {
                return _regionManager.Regions[GlobalConstant.MainContentRegion].ActiveViews.Any();
            }
        }

        public void NavigateTo(string viewName, NavigationParameters? navigationParameters = null)
        {
            var current = GetActiveContentName();

            // Skip if already on the target page
            if (current == viewName)
                return;

            if (!string.IsNullOrEmpty(current))
            {
                _history.Push(current);
            }

            EnsureModuleLoaded(viewName);

            if (navigationParameters != null)
            {
                _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, viewName, navigationParameters);
            }
            else
            {
                _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, viewName);
            }

            Navigated?.Invoke(viewName);
        }

        public void GoBack()
        {
            if (_history.Count > 0)
            {
                var previous = _history.Pop();
                EnsureModuleLoaded(previous);
                _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, previous);
            }
            else
            {
                _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, PageNames.StatsPage);
            }

            var current = GetActiveContentName();
            Navigated?.Invoke(current);
        }

        public void Remove()
        {
            var region = _regionManager.Regions[GlobalConstant.MainContentRegion];
            if (region.Views.Any())
            {
                region.RemoveAll();
                _history.Clear();
                Removed?.Invoke(this, EventArgs.Empty);
                Navigated?.Invoke(string.Empty);
            }
        }

        public string GetActiveContentName()
        {
            var region = _regionManager.Regions[GlobalConstant.MainContentRegion];
            var activeView = region.ActiveViews.FirstOrDefault();
            return activeView?.GetType().Name ?? string.Empty;
        }

        private void EnsureModuleLoaded(string viewName)
        {
            if (!viewName.EndsWith("Page"))
                return;

            var moduleName = viewName.Replace("Page", "Module");
            try
            {
                _moduleManager.LoadModule(moduleName);
            }
            catch
            {
                // Module may already be loaded or doesn't exist as OnDemand
            }
        }
    }
}
