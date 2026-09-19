using System.Windows;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// 把 <see cref="AppSettings.FontScale"/> 档位（0~5）换算为各语义字号 token 的增量，
    /// 覆盖写回 Application.Resources。DynamicResource 引用会在下次布局时自动取新值，
    /// 无需逐页通知。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    internal class UiScaleService : IUiScaleService
    {
        /// <summary>每个语义层级在 0 档（应用默认）时的基准字号。</summary>
        private static readonly (string Key, double Base)[] FontTokens =
        {
            ("FontSize.Caption", 11),
            ("FontSize.Body", 13),
            ("FontSize.Metadata", 12),
            ("FontSize.Section", 14),
            ("FontSize.Title", 16),
            ("FontSize.Display", 20),
        };

        public void Apply(int scale)
        {
            scale = Math.Clamp(scale, 0, IUiScaleService.MaxScale);
            var app = Application.Current;
            if (app?.Resources == null) return;

            // 调用方可能在后台线程（App.OnStartup 里 await 之后的续接不在 UI 线程上），
            // 而 ResourceDictionary 有线程亲和性，直接写会抛"另一个线程拥有该对象"，
            // 结果就是启动时用户选的字号档位根本没生效（要等设置页再改一次才写进去）。
            var dispatcher = app.Dispatcher;
            if (dispatcher is null)
                return;
            if (!dispatcher.CheckAccess())
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                    return;
                dispatcher.Invoke(() => WriteFontTokens(app, scale));
                return;
            }

            WriteFontTokens(app, scale);
        }

        private static void WriteFontTokens(Application app, int scale)
        {
            foreach (var (key, baseSize) in FontTokens)
            {
                app.Resources[key] = baseSize + scale;
            }
        }
    }
}
