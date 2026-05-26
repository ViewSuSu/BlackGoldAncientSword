using System.Reflection;
using NarakaBladepoint.StatsAssistant.Framework.Core.Extensions;

namespace NarakaBladepoint.StatsAssistant.App
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
