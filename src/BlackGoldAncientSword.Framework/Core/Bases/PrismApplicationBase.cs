namespace BlackGoldAncientSword.Framework.Core.Bases
{
    public abstract class PrismApplicationBase : Prism.DryIoc.PrismApplication
    {
        protected sealed override System.Windows.Window CreateShell()
        {
            ContainerProvider = Container;
            return CreateShellExecute();
        }

        protected abstract System.Windows.Window CreateShellExecute();

        public static IContainerProvider ContainerProvider { get; private set; } = null!;
    }
}
