using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Converters
{
    public class BoolToTransparentBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
                return Brushes.Transparent;
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
