using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// unified 接口响应 → Unified 域模型的集中转换。
    /// 后端已在 /app-api/record/unified/* 做完三源（miniProgram/heyBox/dashen）归一化，
    /// 客户端不再按数据源分派；本类仅吸收传输层差异（ISO-8601 时间、字符串 modeCode、number 统计值）。
    /// </summary>
    public static class UnifiedMapper
    {
        // === Search ===

        public static UnifiedSearchResult? MapSearch(SearchRecordResponse? resp)
        {
            var d = resp?.Data;
            if (d == null || string.IsNullOrEmpty(d.RoleIdSimple)) return null;
            return new UnifiedSearchResult
            {
                RoleIdSimple = d.RoleIdSimple ?? string.Empty,
                DataSource = DataSourceExtensions.FromApiString(d.Source),
            };
        }

        // === Player profile ===

        public static UnifiedUserInfo? MapPlayer(PlayerProfile? p)
        {
            if (p == null) return null;
            return new UnifiedUserInfo
            {
                RoleName = p.DisplayName ?? string.Empty,
                RoleLevel = p.Level ?? 0,
                Uid = p.RoleIdSimple ?? string.Empty,
                HeadIcon = p.AvatarUrl ?? string.Empty,
                // unified/player 不返回赛季/各模式段位分；段位数据改由 GetSeasonSummary 提供。
                CurrentSeasonId = null,
                SoloRankScore = null,
                DuoRankScore = null,
                TrioRankScore = null,
            };
        }

        public static UnifiedUserInfo? MapPlayer(GetPlayerProfileResponse? resp) => MapPlayer(resp?.Data);

        // === Season summary (段位 + 统计指标) ===

        public static UnifiedPlayerStats? MapSeasonSummary(GetSeasonSummaryResponse? resp)
        {
            var d = resp?.Data;
            if (d == null) return null;

            var r = d.Rank;
            var grade = r == null ? null : new UnifiedGradeInfo
            {
                GradeName = r.Name ?? string.Empty,
                GradeIcon = r.IconUrl ?? string.Empty,
                GradeScore = r.Score ?? 0,
                GradeLevel = r.Level ?? string.Empty,
            };

            var stats = d.Metrics?.Select(m => new UnifiedStatEntry
            {
                Key = m.Code ?? string.Empty,
                Name = m.Label ?? m.Code ?? string.Empty,
                // 直接用后端 value 原文，与网页一致：该带的单位（如百分率的 "12.5%"）后端已放进 value；
                // unit 是语义标签（count/damage/heal/seconds/%），不可拼接展示（拼了会出现 "12.5%%"、"8count"）。
                Value = m.Value ?? string.Empty,
            }).ToList() ?? new List<UnifiedStatEntry>();

            return new UnifiedPlayerStats { Grade = grade, Stats = stats };
        }

        // === Recent matches ===

        public static List<UnifiedRecentBattleItem> MapRecentMatches(GetRecentMatchesResponse? resp)
        {
            var list = resp?.Data?.Records;
            if (list == null) return new List<UnifiedRecentBattleItem>();
            return list.Select(m =>
            {
                var end = m.Score?.End ?? 0;
                var begin = m.Score?.Begin;
                var delta = m.Score?.Delta ?? 0;
                return new UnifiedRecentBattleItem
                {
                    BattleId = m.DetailKey ?? string.Empty,
                    Rank = (int)(m.Rank ?? 0),
                    HeroIcon = m.Hero?.IconUrl ?? string.Empty,
                    HeroName = m.Hero?.Name ?? string.Empty,
                    GameMode = ModeCodeToBattleApiCode(m.Mode?.Code),
                    // 直接承载后端 mode，供 VM 与网页一致地显示模式名；dashen 源 mode 为 null 时字段留空。
                    ModeName = m.Mode?.Name,
                    ModeCategory = m.Mode?.Category,
                    ModeTeamSize = (int)(m.Mode?.TeamSize ?? 0),
                    Kill = (int)(m.Kills ?? 0),
                    Damage = (int)(m.Damage ?? 0),
                    RoundRankScore = end,
                    BeginRankScore = begin,
                    ScoreDelta = delta,
                    BattleEndTimeMs = ParseIso8601ToMs(m.OccurredAt),
                    Rating = m.Evaluation?.Level ?? string.Empty,
                    RankName = m.Evaluation?.Level ?? string.Empty,
                    HonorTitles = Array.Empty<UnifiedHonorTitle>(),
                };
            }).ToList();
        }

        // === Game modes ===

        public static List<UnifiedMode> MapModes(GetGameModesResponse? resp)
        {
            var list = resp?.Data;
            if (list == null) return new List<UnifiedMode>();
            return list.Select(m => new UnifiedMode
            {
                Code = m.Code ?? string.Empty,
                Name = m.Name ?? string.Empty,
                Category = m.Category ?? string.Empty,
                TeamSize = (int)(m.TeamSize ?? 0),
            }).ToList();
        }

        // === Match detail (personal + team + top5) ===

        public static UnifiedBattleDetail? MapMatchDetail(
            GetMatchDetailResponse? personal,
            GetMatchTeamResponse? team,
            GetMatchTop5Response? top5)
        {
            var p = personal?.Data;
            if (p == null) return null;

            var personalView = new UnifiedPersonalDetail
            {
                HeroName = p.Hero?.Name ?? string.Empty,
                HeroIcon = p.Hero?.IconUrl ?? string.Empty,
                RoleName = p.Player?.DisplayName ?? string.Empty,
                ModeName = p.Mode?.Name,
                Rank = (int)(p.Rank ?? 0),
                BattleEndTimeMs = ParseIso8601ToMs(p.OccurredAt),
                HonorTitles = p.HonorTitles?.Select(MapHonor).ToArray()
                    ?? Array.Empty<UnifiedHonorTitle>(),
                DataList = p.Stats?.Select(MapStat).ToArray()
                    ?? Array.Empty<UnifiedStatEntry>(),
                Weapons = p.Weapons?.Select(MapWeapon).ToArray()
                    ?? Array.Empty<UnifiedWeapon>(),
                SoulItems = p.SoulItems?.Select(MapSoul).ToArray()
                    ?? Array.Empty<UnifiedSoulItem>(),
                Armor = null, // 个人详情无护甲字段（护甲仅在队伍成员上）
            };

            var teamView = team?.Data?.Select(t => new UnifiedTeammate
            {
                HeroIcon = t.Hero?.IconUrl ?? string.Empty,
                HeroName = t.Hero?.Name ?? string.Empty,
                RoleName = t.Player?.DisplayName ?? string.Empty,
                IsMe = t.Me ?? false,
                Armor = t.Armor == null ? null : new UnifiedArmor
                {
                    Icon = t.Armor.IconUrl ?? string.Empty,
                    Level = t.Armor.Level ?? 0,
                },
                Weapons = t.Weapons?.Select(MapWeapon).ToArray() ?? Array.Empty<UnifiedWeapon>(),
                SoulItems = t.SoulItems?.Select(MapSoul).ToArray() ?? Array.Empty<UnifiedSoulItem>(),
                DataList = t.Stats?.Select(MapStat).ToArray() ?? Array.Empty<UnifiedStatEntry>(),
            }).ToArray();

            var top5View = top5?.Data?.Select(e => new UnifiedTop5Entry
            {
                Rank = (int)(e.Rank ?? 0),
                Members = e.Members?.Select(m => new UnifiedTop5Member
                {
                    HeroIcon = m.Hero?.IconUrl ?? string.Empty,
                    HeroName = m.Hero?.Name ?? string.Empty,
                    RoleName = m.DisplayName ?? string.Empty,
                    IsMe = m.Me ?? false,
                }).ToArray() ?? Array.Empty<UnifiedTop5Member>(),
            }).ToArray();

            return new UnifiedBattleDetail
            {
                Personal = personalView,
                Team = teamView,
                Top5 = top5View,
            };
        }

        // === Shared helpers ===

        private static UnifiedHonorTitle MapHonor(HonorTitle h) => new()
        {
            Icon = h.IconUrl ?? string.Empty,
            Name = h.Name ?? string.Empty,
            Desc = h.Description ?? string.Empty,
        };

        private static UnifiedStatEntry MapStat(BattleStat s) => new()
        {
            Key = s.Code ?? string.Empty,
            Name = s.Name ?? s.Code ?? string.Empty,
            Value = FormatStatValue(s.Value),
        };

        private static UnifiedWeapon MapWeapon(Weapon w) => new()
        {
            Icon = w.IconUrl ?? string.Empty,
            Name = w.Name ?? string.Empty,
            Level = w.Level ?? 0,
            Kill = (int)(w.Kills ?? 0),
            Damage = (int)(w.Damage ?? 0),
            Percent = w.Percent ?? 0,
        };

        private static UnifiedSoulItem MapSoul(SoulItem s) => new()
        {
            Icon = s.IconUrl ?? string.Empty,
            Name = s.Name ?? string.Empty,
            Level = s.Level ?? 0,
        };

        private static string FormatStatValue(double? value)
        {
            if (value == null) return string.Empty;
            var v = value.Value;
            // 整数值不带小数点显示，与网页一致。unit 是语义标签（count/damage/seconds），不拼接。
            return v == Math.Floor(v)
                ? ((long)v).ToString(CultureInfo.InvariantCulture)
                : v.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 把 unified 的字符串 modeCode（口径为 battleTidHeyBox，如 "5000000"）归一化为
        /// miniProgram 对局历史 battleApiCode，供 VM 层 FormatGameMode/FromBattleApiCode 统一消费。
        /// 无法识别时返回原始整数值，VM 侧走 Unknown(x) 兜底。
        /// </summary>
        private static int ModeCodeToBattleApiCode(string? modeCode)
        {
            var raw = ParseInt(modeCode);
            if (raw == 0) return 0;
            try
            {
                return GameModeExtensions.FromHeyBoxBattleTid(raw).ToBattleApiCode();
            }
            catch (ArgumentOutOfRangeException)
            {
                return raw;
            }
        }

        private static long ParseIso8601ToMs(string? iso)
        {
            if (string.IsNullOrEmpty(iso)) return 0;
            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dto)
                ? dto.ToUnixTimeMilliseconds()
                : 0;
        }

        private static int ParseInt(string? s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
