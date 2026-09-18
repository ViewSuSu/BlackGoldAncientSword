namespace BlackGoldAncientSword.Framework.Http.Unified
{

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
