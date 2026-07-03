namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 玩家基础信息归一化视图。heyBox 无 CurrentSeasonId / SoloRankScore 等字段时保持 null，
    /// VM 侧按 null 兜底显示 0 或空字符串。
    /// </summary>
    public sealed class UnifiedUserInfo
    {
        public string RoleName { get; init; } = string.Empty;
        public double RoleLevel { get; init; }
        public string Uid { get; init; } = string.Empty;
        public string HeadIcon { get; init; } = string.Empty;
        public double? CurrentSeasonId { get; init; }
        public double? SoloRankScore { get; init; }
        public double? DuoRankScore { get; init; }
        public double? TrioRankScore { get; init; }
    }
}
