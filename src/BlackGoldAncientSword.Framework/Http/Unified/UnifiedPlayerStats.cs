using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 玩家战绩统计归一化视图。heyBox 无独立 stats endpoint，走 HeyBoxUserInfo.overview[] 抽取。
    /// heyBox 分支 Grade 由 <see cref="UnifiedGradeInfo"/> 从 playerInfo.level 反查生成，
    /// 若无法匹配段位名则 GradeScore=0，UI 会显示空段位（占位策略）。
    /// </summary>
    public sealed class UnifiedPlayerStats
    {
        public UnifiedGradeInfo? Grade { get; init; }
        public List<UnifiedStatEntry> Stats { get; init; } = new();
    }

    public sealed class UnifiedGradeInfo
    {
        public string GradeName { get; init; } = string.Empty;
        public string GradeIcon { get; init; } = string.Empty;
        public double GradeScore { get; init; }
        public string GradeLevel { get; init; } = string.Empty;
    }

    /// <summary>
    /// 统一 stats 条目。miniProgram 的 key 是英文（avg_kill/top5_rate 等），
    /// heyBox 的 key 是中文原文（场均击杀/前五率）。Name 优先取 API 提供的显示名，无则退回 key。
    /// </summary>
    public sealed class UnifiedStatEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
