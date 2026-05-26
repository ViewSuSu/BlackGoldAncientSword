using System;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure
{
    public interface IMainContentNavigationService
    {
        bool CanGoBack { get; }
        event Action<string>? Navigated;
        void NavigateTo(string viewName, NavigationParameters? navigationParameters = null);
        void GoBack();
        void Remove();
    }
}
