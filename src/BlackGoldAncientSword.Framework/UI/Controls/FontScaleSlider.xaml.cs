using System.Windows;
using System.Windows.Controls;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    public partial class FontScaleSlider : UserControl
    {
        public static readonly DependencyProperty FontScaleProperty =
            DependencyProperty.Register(nameof(FontScale), typeof(int), typeof(FontScaleSlider),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int FontScale
        {
            get => (int)GetValue(FontScaleProperty);
            set => SetValue(FontScaleProperty, value);
        }

        public FontScaleSlider()
        {
            InitializeComponent();
        }
    }
}
