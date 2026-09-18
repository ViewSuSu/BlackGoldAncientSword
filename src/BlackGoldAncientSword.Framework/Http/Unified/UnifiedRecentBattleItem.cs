using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed class UnifiedRecentBattleItem
    {
        public string BattleId { get; init; } = string.Empty;
        public int Rank { get; init; }
        public string HeroIcon { get; init; } = string.Empty;

        public string HeroId { get; init; } = string.Empty;
        public string HeroName { get; init; } = string.Empty;

        public string MapName { get; init; } = string.Empty;
        public int PlayNum { get; init; }
        public int GameMode { get; init; }
        public string? ModeName { get; init; }
        public string? ModeCategory { get; init; }
        public int ModeTeamSize { get; init; }
        public int Kill { get; init; }
        public int Damage { get; init; }
        public double RoundRankScore { get; init; }
        public double? BeginRankScore { get; init; }
        public long BattleEndTimeMs { get; init; }
        public string Rating { get; init; } = string.Empty;
        public string RankName { get; init; } = string.Empty;
        public double ScoreDelta { get; init; }
        public IReadOnlyList<UnifiedHonorTitle> HonorTitles { get; init; } = System.Array.Empty<UnifiedHonorTitle>();
    }

    public sealed class UnifiedHonorTitle
    {
        public string Icon { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Desc { get; init; } = string.Empty;
    }
}
