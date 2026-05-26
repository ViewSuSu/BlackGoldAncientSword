using System.Globalization;
using System.Windows.Data;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Converters
{
    public class OutRangeEnableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue && parameter is string rangeStr)
            {
                var parts = rangeStr.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int min) && int.TryParse(parts[1], out int max))
                {
                    return intValue >= min && intValue <= max;
                }
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
