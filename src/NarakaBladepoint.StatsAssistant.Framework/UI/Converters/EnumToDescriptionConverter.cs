using System.Globalization;
using System.Windows.Data;
using NarakaBladepoint.StatsAssistant.Framework.Core.Extensions;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Converters
{
    public class EnumToDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enum enumValue)
            {
                return enumValue.GetDescription();
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
