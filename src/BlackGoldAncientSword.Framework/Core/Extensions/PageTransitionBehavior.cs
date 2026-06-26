using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    public static class PageTransitionBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(PageTransitionBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value)
            => element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element)
            => (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Loaded -= OnElementLoaded;
                element.Loaded += OnElementLoaded;
            }
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var element = (FrameworkElement)sender;
                element.Loaded -= OnElementLoaded;

                // Prepare transform (only if not already set)
                element.RenderTransformOrigin = new Point(0.5, 0);
                element.RenderTransform = new TranslateTransform(0, 15);

                var duration = new Duration(TimeSpan.FromMilliseconds(350));
                var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };

                var sb = new Storyboard();

                var opacityAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
                Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

                var yAnim = new DoubleAnimation(15, 0, duration) { EasingFunction = ease };
                Storyboard.SetTargetProperty(yAnim, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));

                sb.Children.Add(opacityAnim);
                sb.Children.Add(yAnim);

                // Must start with Opacity=0 BEFORE beginning the storyboard
                element.Opacity = 0;
                sb.Begin(element);
            }
            catch
            {
                // Graceful fallback — page stays visible without animation
            }
        }
    }
}
