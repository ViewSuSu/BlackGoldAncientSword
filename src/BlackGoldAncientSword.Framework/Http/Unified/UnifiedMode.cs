namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// unified 游戏模式归一化视图。Code 为统一模式编码（口径 battleTidHeyBox）。
    /// </summary>
    public sealed class UnifiedMode
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public int TeamSize { get; init; }
    }
}
