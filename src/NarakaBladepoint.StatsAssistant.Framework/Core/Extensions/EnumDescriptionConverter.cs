using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Extensions
{
    [ValueConversion(typeof(Enum), typeof(string))]
    public class EnumDescriptionConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            var field = value.GetType().GetField(value.ToString()!);
            var attr = field?.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
