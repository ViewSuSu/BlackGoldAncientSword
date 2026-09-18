using System.Collections.Generic;
using System.Linq;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    internal static class UnifiedRosterMapper
    {
        public static IReadOnlyList<UnifiedHeroEntry> MapHeroes(IEnumerable<HeyboxHeroEntry>? heroes)
            => (heroes ?? Enumerable.Empty<HeyboxHeroEntry>())
                .Select(h =>
                {
                    var data = h.Data;
                    return new UnifiedHeroEntry
                    {
                        HeroId = h.HeroId ?? string.Empty,
                        Name = h.Name ?? string.Empty,
                        Icon = h.Img ?? string.Empty,
                        GameCount = (int)UnifiedValueParser.ParseLooseNumber(data?.GameCount),
                        AvgDamage = (int)UnifiedValueParser.ParseLooseNumber(data?.AvgDamage),
                        ChampionRate = data?.Rank1Rate ?? string.Empty,
                        UsePercent = UnifiedValueParser.ParseLooseNumber(data?.UsePercent),
                        Kd = UnifiedValueParser.ParseLooseNumber(data?.Kd),
                        Percent = UnifiedValueParser.ParseLooseNumber(data?.Percent),
                    };
                })
                .ToList();

        public static IReadOnlyList<UnifiedWeaponEntry> MapWeapons(IEnumerable<HeyboxWeaponEntry>? weapons)
            => (weapons ?? Enumerable.Empty<HeyboxWeaponEntry>())
                .Select(w =>
                {
                    var data = w.Data;
                    return new UnifiedWeaponEntry
                    {
                        Name = w.Name ?? string.Empty,
                        Icon = w.Img ?? string.Empty,
                        GameCount = (int)UnifiedValueParser.ParseLooseNumber(data?.GameCount),
                        Round = (int)UnifiedValueParser.ParseLooseNumber(data?.Round),
                        Percent = UnifiedValueParser.ParseLooseNumber(data?.Percent),
                        AvgDamage = data?.AvgDamage ?? string.Empty,
                        KillTimes = (int)UnifiedValueParser.ParseLooseNumber(data?.KillTimes),
                        MaxWeaponDamage = (int)UnifiedValueParser.ParseLooseNumber(data?.MaxWeaponDamage),
                        MaxKill = (int)UnifiedValueParser.ParseLooseNumber(data?.MaxKill),
                    };
                })
                .ToList();
    }
}
