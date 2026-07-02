using System.Reflection;
using BlackGoldAncientSword.Framework.Core.Bases;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;
using Prism.Regions;

namespace BlackGoldAncientSword.Tests.Settings
{
    /// <summary>
    /// 为依赖 <see cref="PrismApplicationBase.ContainerProvider"/> 静态容器的 ViewModel 测试搭建最小 Prism 容器。
    /// <para>
    /// <see cref="PrismApplicationBase.ContainerProvider"/> 的 setter 为 private，测试通过 reflection 注入 mock 容器；
    /// 容器内注册 <see cref="IEventAggregator"/> 与 <see cref="IRegionManager"/>，供 ViewModelBase ctor 解析。
    /// </para>
    /// </summary>
    public sealed class PrismTestFixture
    {
        public PrismTestFixture()
        {
            // 已初始化过则跳过：xUnit 并行测试类可能重复实例化 collection fixture 前的检查
            if (PrismApplicationBase.ContainerProvider != null)
                return;

            var extension = new DryIocContainerExtension();
            extension.RegisterInstance<IEventAggregator>(new EventAggregator());
            extension.RegisterInstance<IRegionManager>(new Mock<IRegionManager>().Object);

            var prop = typeof(PrismApplicationBase).GetProperty(
                nameof(PrismApplicationBase.ContainerProvider),
                BindingFlags.Public | BindingFlags.Static)!;
            prop.SetValue(null, extension);
        }
    }

    [CollectionDefinition(nameof(PrismTestCollection))]
    public sealed class PrismTestCollection : ICollectionFixture<PrismTestFixture>
    {
        // xUnit collection 空定义，仅用于共享 fixture
    }
}
