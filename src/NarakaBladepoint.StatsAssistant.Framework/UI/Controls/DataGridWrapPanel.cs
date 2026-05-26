using System;
using System.Windows;
using System.Windows.Controls;

namespace NarakaBladepoint.StatsAssistant.Framework.UI.Controls
{
    /// <summary>
    /// WrapPanel that properly constrains its width during measure,
    /// fixing the WPF DataGrid clipping issue where WrapPanel reports
    /// single-row height due to infinite available width.
    /// </summary>
    public class DataGridWrapPanel : WrapPanel
    {
        protected override Size MeasureOverride(Size constraint)
        {
            if (double.IsPositiveInfinity(constraint.Width))
            {
                // Walk up to find the DataGridCell to get its actual width
                var cell = FindParent<DataGridCell>(this);
                if (cell != null && !double.IsNaN(cell.ActualWidth) && cell.ActualWidth > 0)
                {
                    constraint.Width = cell.ActualWidth - Margin.Left - Margin.Right;
                }
                else
                {
                    // Fallback: use 160px as default column width
                    constraint.Width = 160;
                }
            }
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // After arrange, if the desired height changed, invalidate measure
            // to ensure the DataGrid row resizes on subsequent passes
            var result = base.ArrangeOverride(finalSize);
            return result;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T t) return t;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}