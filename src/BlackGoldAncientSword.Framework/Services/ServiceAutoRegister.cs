using System.Reflection;
using BlackGoldAncientSword.Framework.Core.Extensions;

namespace BlackGoldAncientSword.Framework.Services
{
    public static class ServiceAutoRegister
    {
        private static readonly Assembly _assembly;

        static ServiceAutoRegister()
        {
            _assembly = typeof(ServiceAutoRegister).Assembly;
        }

        public static IContainerRegistry RegisterFrameworkServices(
            this IContainerRegistry containerRegistry)
        {
            return containerRegistry.RegisterComponentsByAssembly(_assembly);
        }
    }
}
