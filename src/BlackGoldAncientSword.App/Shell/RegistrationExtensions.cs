using System.Reflection;
using BlackGoldAncientSword.Framework.Core.Extensions;

namespace BlackGoldAncientSword.App
{
    internal static class RegistrationExtensions
    {
        private static readonly Assembly AppAssembly;

        static RegistrationExtensions()
        {
            AppAssembly = typeof(RegistrationExtensions).Assembly;
        }

        public static IContainerRegistry RegisterAppLayer(this IContainerRegistry containerRegistry)
        {
            return containerRegistry.RegisterComponentsByAssembly(AppAssembly);
        }
    }
}
