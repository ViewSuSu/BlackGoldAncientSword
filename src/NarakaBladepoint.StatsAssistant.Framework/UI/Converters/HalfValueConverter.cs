using System.Globalization;
using System.Windows.Data;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Converters
{
    public class HalfValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
                return doubleValue / 2.0;
            if (value is int intValue)
                return intValue / 2;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
