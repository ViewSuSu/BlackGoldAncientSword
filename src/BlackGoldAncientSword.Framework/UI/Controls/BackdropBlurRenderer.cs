using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    /// <summary>
    /// 为浮层遮罩生成"亚克力"模糊背景：抓取所在窗口内容 → 降采样缩小 → 高斯模糊。
    /// 结果是一张 ImageBrush，可铺在 <see cref="OverlayHost"/> 遮罩层后面，形成毛玻璃观感。
    /// 模糊的是窗口静态截图而非 DWM 实时合成，因此主窗口其它内容无需任何改动，
    /// 且不依赖系统"透明效果"开关，Win10 / Win11 均生效。
    /// </summary>
    internal static class BackdropBlurRenderer
    {
        private const int MaxSourceWidth = 800;   // 目标截图宽度上限（降采样）
        private const double BlurRadius = 24;      // 高斯模糊半径

        /// <summary>抓取窗口内容并生成模糊后的背景画刷；任何失败静默返回 null。</summary>
        public static ImageBrush? CaptureBlurredBackdrop(Window window)
        {
            try
            {
                var content = window.Content as Visual;
                if (content == null)
                    return null;

                var bounds = VisualTreeHelper.GetDescendantBounds(content);
                var width = (int)Math.Ceiling(bounds.Width);
                var height = (int)Math.Ceiling(bounds.Height);
                if (width <= 0 || height <= 0)
                    return null;

                // 1) 缩放到合适大小（保留宽高比），降低后续模糊成本
                var scale = width > MaxSourceWidth ? (double)MaxSourceWidth / width : 1d;
                var smallW = Math.Max(1, (int)(width * scale));
                var smallH = Math.Max(1, (int)(height * scale));

                // 2) 绘制整窗内容（含子视觉）到降采样位图
                var small = new RenderTargetBitmap(smallW, smallH, 96, 96, PixelFormats.Pbgra32);
                var smallVisual = new DrawingVisual();
                using (var dc = smallVisual.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, smallW, smallH));
                small.Render(smallVisual);

                // 3) 对降采样图做高斯模糊：把位图放进带 BlurEffect 的 Image 再整体渲染
                var blurred = new RenderTargetBitmap(smallW, smallH, 96, 96, PixelFormats.Pbgra32);
                var blurHost = new System.Windows.Controls.Image
                {
                    Source = small,
                    Stretch = Stretch.Fill,
                    Effect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = BlurRadius,
                        KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                    },
                };
                blurHost.Measure(new Size(smallW, smallH));
                blurHost.Arrange(new Rect(0, 0, smallW, smallH));
                blurred.Render(blurHost);

                return new ImageBrush(blurred)
                {
                    Stretch = Stretch.UniformToFill,
                    TileMode = TileMode.None,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
