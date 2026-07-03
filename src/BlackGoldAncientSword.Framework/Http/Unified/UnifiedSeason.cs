namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 赛季条目的归一化视图。seasons endpoint 两套共享，DTO 直接透传。
    /// </summary>
    public sealed class UnifiedSeason
    {
        public double Code { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? HeyBoxSeasonName { get; init; }
    }
}
