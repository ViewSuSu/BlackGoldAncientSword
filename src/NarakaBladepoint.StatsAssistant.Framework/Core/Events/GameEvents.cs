namespace NarakaBladepoint.StatsAssistant.Framework.Core.Events
{
    public class NavigationArgs(string viewName, NavigationParameters? navigationParameter = null)
    {
        public string ViewName { get; } = viewName;
        public NavigationParameters? Parameter { get; } = navigationParameter;
    }

    public class LoadHomePageRegionEvent : PubSubEvent<NavigationArgs> { }
    public class RemoveHomePageRegionEvent : PubSubEvent { }
    public class RemoveAllHomePageRegionEvent : PubSubEvent { }
}
