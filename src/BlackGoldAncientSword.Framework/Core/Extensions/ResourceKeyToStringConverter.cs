using System.Globalization;
using System.Windows.Data;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    public class ResourceKeyToStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string key && System.Windows.Application.Current != null)
                return System.Windows.Application.Current.TryFindResource(key) ?? key;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
