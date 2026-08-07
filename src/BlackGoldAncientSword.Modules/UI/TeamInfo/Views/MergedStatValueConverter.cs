using System.Globalization;
using System.Windows.Data;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Views
{
    /// <summary>diff 对比列取统计行哪一侧的对比值。</summary>
    public enum TeamDiffSide
    {
        None = 0,
        Left = 1,
        Right = 2,
    }

    /// <summary>
    /// 从 <see cref="MergedStatRow"/> 中按卡片下标取对应列的值。
    /// 卡片通过 <see cref="TeamMemberCard.MemberIndex"/> 决定取 Val0/Val1/Val2 中的哪一列。
    /// </summary>
    public class MergedStatValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is MergedStatRow row && values[1] is int index)
            {
                return index switch
                {
                    0 => row.Val0,
                    1 => row.Val1,
                    2 => row.Val2,
                    _ => "-",
                };
            }
            return "-";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 取 <see cref="MergedStatRow"/> 某侧 diff 的显示文本（DiffLeftText / DiffRightText）。
    /// <see cref="TeamDiffSide"/> 决定取哪一侧。
    /// </summary>
    public class TeamDiffValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is MergedStatRow row && values[1] is TeamDiffSide side)
            {
                return side switch
                {
                    TeamDiffSide.Left => row.DiffLeftText,
                    TeamDiffSide.Right => row.DiffRightText,
                    _ => string.Empty,
                };
            }
            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 取 <see cref="MergedStatRow"/> 某侧 diff 的颜色（DiffLeftColor / DiffRightColor）。
    /// <see cref="TeamDiffSide"/> 决定取哪一侧。返回 <see cref="Brush"/> 而非 hex 字符串，
    /// 否则绑定到 Foreground（Brush 类型）时运行时不会自动做 string→Brush 转换，颜色显示为默认黑。
    /// </summary>
    public class TeamDiffColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var hex = "#999999";
            if (values.Length >= 2 && values[0] is MergedStatRow row && values[1] is TeamDiffSide side)
            {
                hex = side switch
                {
                    TeamDiffSide.Left => row.DiffLeftColor,
                    TeamDiffSide.Right => row.DiffRightColor,
                    _ => "#999999",
                };
            }
            try
            {
                var brush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
            }
            catch
            {
                return System.Windows.Media.Brushes.Gray;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
