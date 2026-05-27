using System.Reflection;

namespace BlackGoldAncientSword.Modules
{
    public static class ModuleCatalogConfigManager
    {
        private static readonly Assembly _assembly;

        static ModuleCatalogConfigManager()
        {
            _assembly = typeof(ModuleCatalogConfigManager).Assembly;
        }

        public static IContainerRegistry RegisterModuleLayer(this IContainerRegistry containerRegistry)
        {
            return containerRegistry.RegisterComponentsByAssembly(_assembly);
        }

        public static ModuleCatalog ConfigAll()
        {
            return CreateModuleCatalogFromAssembly(_assembly);
        }

        private static IEnumerable<Type> GetModuleTypes(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            return assembly
                .GetTypes()
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
        }

        private static ModuleCatalog CreateModuleCatalogFromAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            var catalog = new ModuleCatalog();
            var moduleTypes = GetModuleTypes(assembly);

            foreach (var moduleType in moduleTypes)
            {
                // Prism reads [Module(OnDemand = true)] from the type to determine InitializationMode
                catalog.AddModule(moduleType);
            }

            return catalog;
        }
    }
}
