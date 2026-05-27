namespace BlackGoldAncientSword.Framework.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ComponentAttribute : Attribute
    {
        public ComponentAttribute(
            ComponentLifetime lifetime = ComponentLifetime.Transient,
            bool registerSelf = false
        )
        {
            Lifetime = lifetime;
            RegisterSelf = registerSelf;
        }

        public ComponentLifetime Lifetime { get; }
        public bool RegisterSelf { get; }
    }

    public enum ComponentLifetime
    {
        Transient,
        Singleton,
        Scoped
    }
}
