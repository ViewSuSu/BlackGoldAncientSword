using System.Reflection;
using NarakaBladepoint.StatsAssistant.Framework.Core.Extensions;

namespace NarakaBladepoint.StatsAssistant.Framework.Services
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
