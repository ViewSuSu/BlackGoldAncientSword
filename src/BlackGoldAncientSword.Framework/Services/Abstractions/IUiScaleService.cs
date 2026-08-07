namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 全局字号缩放服务。把 settings 里的 FontScale 档位（0~5）换算成
    /// 各语义字号 token 的增量，写入 Application.Resources，使所有
    /// DynamicResource 引用字号的界面即时刷新。
    /// </summary>
    public interface IUiScaleService
    {
        /// <summary>字号缩放的最大档位（每档 +1px）。</summary>
        const int MaxScale = 5;

        /// <summary>按档位应用字号缩放。必须在 UI 线程调用。范围会被钳制到 [0, MaxScale]。</summary>
        void Apply(int scale);
    }
}
