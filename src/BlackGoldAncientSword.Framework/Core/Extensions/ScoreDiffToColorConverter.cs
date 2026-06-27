using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    [ValueConversion(typeof(double), typeof(Brush))]
    public class ScoreDiffToColorConverter : IValueConverter
    {
        private static readonly Brush PositiveBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xAA, 0x22));
        private static readonly Brush NegativeBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0x33, 0x33));
        private static readonly Brush ZeroBrush = new SolidColorBrush(Colors.Gray);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double diff)
            {
                if (diff > 0) return PositiveBrush;
                if (diff < 0) return NegativeBrush;
            }
            return ZeroBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
