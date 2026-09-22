using System;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Services;
using Prism.DryIoc;
using Prism.Ioc;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    public class HeyboxSessionRestorerRegistrationTests
    {
        [Fact]
        public void Container_Resolves_Restorer_From_Framework_Components()
        {
            var container = NewContainer();

            Assert.NotNull(container.Resolve<IHeyboxSessionRestorer>());
        }

        [Fact]
        public async Task Resolved_Restorer_Should_Run_The_Real_Probe()
        {
            var container = NewContainer();
            var restorer = container.Resolve<IHeyboxSessionRestorer>();

            var task = restorer.TryRestoreAsync(
                new HeyboxSession("99365688", "secret-pkey"), string.Empty, string.Empty);

            var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(task, finished);
        }

        private static DryIocContainerExtension NewContainer()
        {
            var container = new DryIocContainerExtension();
            container.RegisterSingleton<IHeyboxSessionState, HeyboxSessionState>();
            container.RegisterComponentsByAssembly(typeof(ServiceAutoRegister).Assembly);
            return container;
        }
    }
}
