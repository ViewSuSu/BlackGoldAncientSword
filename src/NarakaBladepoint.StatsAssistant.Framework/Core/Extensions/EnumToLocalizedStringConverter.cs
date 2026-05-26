using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Extensions
{
    /// <summary>
    /// Converts an enum value to a localized string by looking up
    /// "{Prefix}{EnumName}" from Application resources.
    /// ConverterParameter = resource key prefix (e.g. "Mode.")
    /// </summary>
    [ValueConversion(typeof(Enum), typeof(string))]
    public class EnumToLocalizedStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            var prefix = parameter as string ?? string.Empty;
            var key = prefix + value.ToString();

            return Application.Current?.TryFindResource(key) as string
                   ?? value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}