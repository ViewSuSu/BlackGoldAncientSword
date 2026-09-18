using System;
using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed class UnifiedPlayerStats
    {
        public UnifiedGradeInfo? Grade { get; init; }

        public List<UnifiedStatEntry> Stats { get; init; } = new();

        public double? CompositeScore { get; init; }

        public string ScoreGrade { get; init; } = string.Empty;

        public IReadOnlyList<UnifiedScoreItem> ScoreList { get; init; } = Array.Empty<UnifiedScoreItem>();

        public IReadOnlyList<UnifiedStatEntry> ScoreStats { get; init; } = Array.Empty<UnifiedStatEntry>();

        public double RecentAvgRank { get; init; }

        public IReadOnlyList<UnifiedRecentRank> RecentRanks { get; init; } = Array.Empty<UnifiedRecentRank>();

        public IReadOnlyList<UnifiedHeroEntry> Heroes { get; init; } = Array.Empty<UnifiedHeroEntry>();

        public IReadOnlyList<UnifiedWeaponEntry> Weapons { get; init; } = Array.Empty<UnifiedWeaponEntry>();
    }

    public sealed class UnifiedGradeInfo
    {
        public string GradeName { get; init; } = string.Empty;
        public string GradeIcon { get; init; } = string.Empty;
        public double GradeScore { get; init; }
        public string GradeLevel { get; init; } = string.Empty;
    }

    public sealed class UnifiedStatEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;

        public string Grade { get; init; } = string.Empty;
    }

    public sealed class UnifiedScoreItem
    {
        public string Name { get; init; } = string.Empty;
        public double Score { get; init; }
    }

    public sealed class UnifiedRecentRank
    {
        public string MatchId { get; init; } = string.Empty;
        public int Rank { get; init; }
    }
}
