using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 单局详情的归一化视图。miniProgram 三接口（person/team/top5）与 heyBox 单接口（detail）
    /// 统一映射到此。heyBox 分支 Team/Top5 为 null，VM 侧显示空占位。
    /// </summary>
    public sealed class UnifiedBattleDetail
    {
        public UnifiedPersonalDetail Personal { get; init; } = new();
        public IReadOnlyList<UnifiedTeammate>? Team { get; init; }
        public IReadOnlyList<UnifiedTop5Entry>? Top5 { get; init; }
    }

    public sealed class UnifiedPersonalDetail
    {
        public string HeroName { get; init; } = string.Empty;
        public string HeroIcon { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        /// <summary>后端 match.mode.name（完整模式名，如"天选三排"）。与网页一致直接展示；dashen 源 mode 为 null 时为空。</summary>
        public string? ModeName { get; init; }
        public int Rank { get; init; }
        public long BattleEndTimeMs { get; init; }
        public IReadOnlyList<UnifiedHonorTitle> HonorTitles { get; init; } = System.Array.Empty<UnifiedHonorTitle>();
        public IReadOnlyList<UnifiedStatEntry> DataList { get; init; } = System.Array.Empty<UnifiedStatEntry>();
        public IReadOnlyList<UnifiedWeapon> Weapons { get; init; } = System.Array.Empty<UnifiedWeapon>();
        public IReadOnlyList<UnifiedSoulItem> SoulItems { get; init; } = System.Array.Empty<UnifiedSoulItem>();
        public UnifiedArmor? Armor { get; init; }
    }

    public sealed class UnifiedTeammate
    {
        public string HeroIcon { get; init; } = string.Empty;
        public string HeroName { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        public bool IsMe { get; init; }
        public UnifiedArmor? Armor { get; init; }
        public IReadOnlyList<UnifiedWeapon> Weapons { get; init; } = System.Array.Empty<UnifiedWeapon>();
        public IReadOnlyList<UnifiedSoulItem> SoulItems { get; init; } = System.Array.Empty<UnifiedSoulItem>();
        public IReadOnlyList<UnifiedStatEntry> DataList { get; init; } = System.Array.Empty<UnifiedStatEntry>();
    }

    public sealed class UnifiedTop5Entry
    {
        public int Rank { get; init; }
        public IReadOnlyList<UnifiedTop5Member> Members { get; init; } = System.Array.Empty<UnifiedTop5Member>();
    }

    public sealed class UnifiedTop5Member
    {
        public string HeroIcon { get; init; } = string.Empty;
        public string HeroName { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        public bool IsMe { get; init; }
    }

    public sealed class UnifiedWeapon
    {
        public string Icon { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public double Level { get; init; }
        public int Kill { get; init; }
        public int Damage { get; init; }
        public double Percent { get; init; }
    }

    public sealed class UnifiedSoulItem
    {
        public string Icon { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public double Level { get; init; }
    }

    public sealed class UnifiedArmor
    {
        public string Icon { get; init; } = string.Empty;
        public double Level { get; init; }
    }
}
