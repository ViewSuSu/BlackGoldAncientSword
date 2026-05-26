using System.Globalization;
using System.Windows.Data;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Converters
{
    public class ImageSourceToFileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path)
            {
                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
