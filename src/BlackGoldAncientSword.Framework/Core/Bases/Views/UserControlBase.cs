using System.Windows.Controls;

namespace BlackGoldAncientSword.Framework.Core.Bases.Views
{
    public abstract class UserControlBase : UserControl
    {
        protected UserControlBase()
        {
            ViewModelLocator.SetAutoWireViewModel(this, true);
        }
    }
}
