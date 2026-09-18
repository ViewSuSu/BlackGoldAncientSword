namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed class UnifiedHeroEntry
    {
        public string HeroId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;

        public int GameCount { get; init; }

        public int AvgDamage { get; init; }

        public string ChampionRate { get; init; } = string.Empty;

        public double UsePercent { get; init; }

        public double Kd { get; init; }

        public double Percent { get; init; }
    }

    public sealed class UnifiedWeaponEntry
    {
        public string Name { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;

        public int GameCount { get; init; }

        public int Round { get; init; }

        public double Percent { get; init; }

        public string AvgDamage { get; init; } = string.Empty;

        public int KillTimes { get; init; }

        public int MaxWeaponDamage { get; init; }

        public int MaxKill { get; init; }
    }
}
