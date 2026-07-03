using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 最近对局条目的归一化视图。
    /// BattleId 统一为字符串以承载 miniProgram 的数值 battleId 与 heyBox 的 matchId。
    /// BattleEndTimeMs 始终为毫秒（heyBox 的秒级 time * 1000）。
    /// heyBox 无 BeginRankScore，填 null。
    /// </summary>
    public sealed class UnifiedRecentBattleItem
    {
        public string BattleId { get; init; } = string.Empty;
        public int Rank { get; init; }
        public string HeroIcon { get; init; } = string.Empty;
        public string HeroName { get; init; } = string.Empty;
        /// <summary>miniProgram 分支来自 subtype/gameMode（对局 API 编码），heyBox 分支来自 battleTid。</summary>
        public int GameMode { get; init; }
        public int Kill { get; init; }
        public int Damage { get; init; }
        public double RoundRankScore { get; init; }
        public double? BeginRankScore { get; init; }
        public long BattleEndTimeMs { get; init; }
        public string Rating { get; init; } = string.Empty;
        public IReadOnlyList<UnifiedHonorTitle> HonorTitles { get; init; } = System.Array.Empty<UnifiedHonorTitle>();
    }

    public sealed class UnifiedHonorTitle
    {
        public string Icon { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Desc { get; init; } = string.Empty;
    }
}
