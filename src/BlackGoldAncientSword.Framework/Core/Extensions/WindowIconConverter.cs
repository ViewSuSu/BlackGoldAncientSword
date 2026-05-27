using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    /// <summary>
    /// 将 Window.Icon 绑定到 Image.Source 的转换器。
    /// 直接返回 Window 图标，无需额外转换；
    /// 若图标为 null 则返回 null。
    /// </summary>
    [ValueConversion(typeof(ImageSource), typeof(ImageSource))]
    public class WindowIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value as ImageSource;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
