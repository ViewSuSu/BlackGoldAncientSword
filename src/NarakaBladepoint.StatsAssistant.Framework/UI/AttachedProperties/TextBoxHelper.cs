using System.Windows;
using System.Windows.Controls;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.AttachedProperties
{
    public static class TextBoxHelper
    {
        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.RegisterAttached(
                "PlaceholderText",
                typeof(string),
                typeof(TextBoxHelper),
                new PropertyMetadata(string.Empty)
            );

        public static string GetPlaceholderText(TextBox element)
        {
            return (string)element.GetValue(PlaceholderTextProperty);
        }

        public static void SetPlaceholderText(TextBox element, string value)
        {
            element.SetValue(PlaceholderTextProperty, value);
        }
    }
}
