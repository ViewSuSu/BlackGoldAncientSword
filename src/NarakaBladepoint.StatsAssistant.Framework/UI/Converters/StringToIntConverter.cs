using System.Globalization;
using System.Windows.Data;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Converters
{
    public class StringToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() ?? "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (int.TryParse(value?.ToString(), out int result))
                return result;
            return 0;
        }
    }
}
