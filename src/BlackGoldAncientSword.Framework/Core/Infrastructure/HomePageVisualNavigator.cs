using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    [Component(ComponentLifetime.Singleton)]
    public class HomePageVisualNavigator
    {
        private readonly IRegionManager _regionManager;

        private static readonly string[] HomePageRegions = new[]
        {
            GlobalConstant.HomePageRegion1,
            GlobalConstant.HomePageRegion2,
            GlobalConstant.HomePageRegion3,
            GlobalConstant.HomePageRegion4,
        };

        public HomePageVisualNavigator(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        public bool HasActiveRegion
        {
            get
            {
                return HomePageRegions.Any(r =>
                {
                    var region = _regionManager.Regions[r];
                    return region != null && region.ActiveViews.Any();
                });
            }
        }

        public void RequestNavigate(string viewName, NavigationParameters? navigationParameters = null)
        {
            foreach (var regionName in HomePageRegions)
            {
                var region = _regionManager.Regions[regionName];
                if (region == null || !region.ActiveViews.Any())
                {
                    if (navigationParameters != null)
                    {
                        _regionManager.RequestNavigate(regionName, viewName, navigationParameters);
                    }
                    else
                    {
                        _regionManager.RequestNavigate(regionName, viewName);
                    }
                    return;
                }
            }
        }

        public void RemoveTop()
        {
            for (int i = HomePageRegions.Length - 1; i >= 0; i--)
            {
                var region = _regionManager.Regions[HomePageRegions[i]];
                if (region != null && region.ActiveViews.Any())
                {
                    region.RemoveAll();
                    break;
                }
            }
        }

        public void RemoveAll()
        {
            foreach (var regionName in HomePageRegions)
            {
                var region = _regionManager.Regions[regionName];
                region?.RemoveAll();
            }
        }
    }
}
