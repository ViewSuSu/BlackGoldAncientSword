using System.Windows.Controls;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Bases.Views
{
    public abstract class UserControlBase : UserControl
    {
        protected UserControlBase()
        {
            ViewModelLocator.SetAutoWireViewModel(this, true);
        }
    }
}
