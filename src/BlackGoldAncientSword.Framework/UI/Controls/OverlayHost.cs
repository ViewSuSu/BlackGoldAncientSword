using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Prism.Ioc;
using Prism.Regions;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    /// <summary>
    /// 通用浮层壳：半透明遮罩 + 居中白色卡片 + 标题 + 右上关闭按钮 + Esc 关闭。
    /// 子页面仅需提供 <see cref="ContentControl.Content"/> 卡片正文与 <see cref="RegionName"/>。
    /// 关闭时清空对应 Prism Region，避免每个浮层页面都重复一份 dismiss 模板代码。
    /// </summary>
    public class OverlayHost : ContentControl
    {
        private const string PartCloseButton = "PART_CloseButton";

        private ButtonBase? _closeButton;

        static OverlayHost()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(OverlayHost),
                new FrameworkPropertyMetadata(typeof(OverlayHost)));
        }

        public OverlayHost()
        {
            Focusable = true;
            Loaded += OnLoaded;
            KeyDown += OnKeyDown;
        }

        public static readonly DependencyProperty RegionNameProperty =
            DependencyProperty.Register(
                nameof(RegionName),
                typeof(string),
                typeof(OverlayHost),
                new PropertyMetadata(null));

        /// <summary>关闭时要 RemoveAll 的 Prism Region 名称。</summary>
        public string? RegionName
        {
            get => (string?)GetValue(RegionNameProperty);
            set => SetValue(RegionNameProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(OverlayHost),
                new PropertyMetadata(null));

        public string? Title
        {
            get => (string?)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty CardMaxWidthProperty =
            DependencyProperty.Register(
                nameof(CardMaxWidth),
                typeof(double),
                typeof(OverlayHost),
                new PropertyMetadata(520.0));

        public double CardMaxWidth
        {
            get => (double)GetValue(CardMaxWidthProperty);
            set => SetValue(CardMaxWidthProperty, value);
        }

        public static readonly DependencyProperty CardMinWidthProperty =
            DependencyProperty.Register(
                nameof(CardMinWidth),
                typeof(double),
                typeof(OverlayHost),
                new PropertyMetadata(380.0));

        public double CardMinWidth
        {
            get => (double)GetValue(CardMinWidthProperty);
            set => SetValue(CardMinWidthProperty, value);
        }

        public static readonly DependencyProperty CardMaxHeightProperty =
            DependencyProperty.Register(
                nameof(CardMaxHeight),
                typeof(double),
                typeof(OverlayHost),
                new PropertyMetadata(double.PositiveInfinity));

        public double CardMaxHeight
        {
            get => (double)GetValue(CardMaxHeightProperty);
            set => SetValue(CardMaxHeightProperty, value);
        }

        public static readonly DependencyProperty CardPaddingProperty =
            DependencyProperty.Register(
                nameof(CardPadding),
                typeof(Thickness),
                typeof(OverlayHost),
                new PropertyMetadata(new Thickness(20, 16, 20, 16)));

        public Thickness CardPadding
        {
            get => (Thickness)GetValue(CardPaddingProperty);
            set => SetValue(CardPaddingProperty, value);
        }

        public static readonly DependencyProperty EscapeDismissesProperty =
            DependencyProperty.Register(
                nameof(EscapeDismisses),
                typeof(bool),
                typeof(OverlayHost),
                new PropertyMetadata(true));

        /// <summary>Esc 是否触发关闭。需要确认丢弃编辑等场景可设 false。</summary>
        public bool EscapeDismisses
        {
            get => (bool)GetValue(EscapeDismissesProperty);
            set => SetValue(EscapeDismissesProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_closeButton != null)
                _closeButton.Click -= OnCloseButtonClick;

            _closeButton = GetTemplateChild(PartCloseButton) as ButtonBase;

            if (_closeButton != null)
                _closeButton.Click += OnCloseButtonClick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Focus();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (EscapeDismisses && e.Key == Key.Escape)
            {
                Dismiss();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 关闭按钮 / Esc 触发关闭前的可拦截钩子。宿主页面订阅后可通过
        /// <see cref="CancelEventArgs.Cancel"/>=true 阻止关闭（例如弹确认对话框）。
        /// </summary>
        public event EventHandler<CancelEventArgs>? Closing;

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            var args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel) return;
            Dismiss();
        }

        /// <summary>清空所属 Region。Dismiss 容错：Region 不存在或容器未就绪时静默。</summary>
        public void Dismiss()
        {
            var regionName = RegionName;
            if (string.IsNullOrEmpty(regionName))
                return;

            try
            {
                var rm = BlackGoldAncientSword.Framework.Core.Bases.PrismApplicationBase.ContainerProvider.Resolve<IRegionManager>();
                if (rm.Regions.ContainsRegionWithName(regionName))
                    rm.Regions[regionName].RemoveAll();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(OverlayHost)}.{nameof(Dismiss)}] region={regionName} failed: {ex.Message}");
            }
        }
    }
}
