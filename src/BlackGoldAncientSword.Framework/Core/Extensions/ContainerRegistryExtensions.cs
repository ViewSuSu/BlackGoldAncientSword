using System.Reflection;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    public static class ContainerRegistryExtensions
    {
        public static IContainerRegistry RegisterComponentsByAssembly(
            this IContainerRegistry registry,
            Assembly assembly
        )
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            var componentTypes = assembly
                .GetTypes()
                .Where(t =>
                    t.IsClass && !t.IsAbstract && t.IsDefined(typeof(Attributes.ComponentAttribute), false)
                );

            foreach (var componentType in componentTypes)
            {
                RegisterComponent(registry, componentType);
            }

            return registry;
        }

        private static void RegisterComponent(IContainerRegistry registry, Type componentType)
        {
            var attribute =
                componentType
                    .GetCustomAttributes(typeof(Attributes.ComponentAttribute), false)
                    .FirstOrDefault() as Attributes.ComponentAttribute;

            if (attribute == null)
                return;

            var interfaces = componentType
                .GetInterfaces()
                .Where(i => i != typeof(IDisposable) && i != typeof(IAsyncDisposable))
                .ToList();

            if (interfaces.Count > 0)
            {
                foreach (var interfaceType in interfaces)
                {
                    RegisterWithLifetime(registry, interfaceType, componentType, attribute.Lifetime);
                }
            }

            if (attribute.RegisterSelf || interfaces.Count == 0)
            {
                RegisterWithLifetime(registry, componentType, componentType, attribute.Lifetime);
            }
        }

        private static void RegisterWithLifetime(
            IContainerRegistry registry,
            Type fromType,
            Type toType,
            Attributes.ComponentLifetime lifetime
        )
        {
            switch (lifetime)
            {
                case Attributes.ComponentLifetime.Singleton:
                    registry.RegisterSingleton(fromType, toType);
                    break;
                case Attributes.ComponentLifetime.Transient:
                    registry.Register(fromType, toType);
                    break;
                case Attributes.ComponentLifetime.Scoped:
                    registry.RegisterScoped(fromType, toType);
                    break;
                default:
                    registry.Register(fromType, toType);
                    break;
            }
        }
    }
}
