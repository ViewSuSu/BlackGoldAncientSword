using System;
using System.Collections.Generic;
using System.Linq;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 两套 API 响应 → Unified 域模型的集中转换。
    /// miniProgram 与 heyBox 各持一套映射方法，字段差异（key/desc 语言、id 类型、时间单位、
    /// 段位对象缺失）在此吸收，loader/VM 层不再感知源差。
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
                RoleName = d.RoleName ?? string.Empty,
                Avatar = d.Avatar ?? string.Empty,
                RoleLevel = d.RoleLevel ?? 0,
                DataSource = DataSourceExtensions.FromApiString(d.DataSource),
                LevelName = d.LevelName ?? string.Empty,
                LevelImg = d.LevelImg ?? string.Empty,
            };
        }

        // === User info ===

        public static UnifiedUserInfo? MapMiniProgramUser(GetUserInfoResponse? resp)
        {
            var d = resp?.Data;
            if (d == null) return null;
            return new UnifiedUserInfo
            {
                RoleName = d.Role?.RoleName ?? d.NickName ?? string.Empty,
                RoleLevel = d.Role?.RoleLevel ?? 0,
                Uid = d.Role?.Uid ?? string.Empty,
                HeadIcon = d.Role?.HeadIcon ?? d.AvatarUrl ?? string.Empty,
                CurrentSeasonId = d.CurrentSeasonId,
                SoloRankScore = d.SurviveSingleGrade,
                DuoRankScore = d.SurviveDoubleGrade,
                TrioRankScore = d.SurviveTriplexGrade,
            };
        }

        public static UnifiedUserInfo? MapHeyBoxUser(HeyBoxUserInfoResponse? resp, string roleIdSimple)
        {
            var d = resp?.Data;
            var p = d?.PlayerInfo;
            if (d == null || p == null) return null;
            return new UnifiedUserInfo
            {
                RoleName = p.Name ?? string.Empty,
                RoleLevel = ParseDouble(p.Lv),
                Uid = roleIdSimple,
                HeadIcon = p.Avatar ?? string.Empty,
                // heyBox 无 seasonId / 三排段位分：全部留 null，UI 层退化为占位
                CurrentSeasonId = null,
                SoloRankScore = null,
                DuoRankScore = null,
                TrioRankScore = null,
            };
        }

        // === Player stats ===

        public static UnifiedPlayerStats? MapMiniProgramStats(GetPlayerStatsResponse? resp)
        {
            var d = resp?.Data;
            if (d == null) return null;

            var grade = d.Grade == null ? null : new UnifiedGradeInfo
            {
                GradeName = d.Grade.GradeName ?? string.Empty,
                GradeIcon = d.Grade.GradeIcon ?? string.Empty,
                GradeScore = d.Grade.GradeScore ?? 0,
                GradeLevel = d.Grade.GradeLevel ?? string.Empty,
            };

            var stats = d.Stats?.Select(s => new UnifiedStatEntry
            {
                Key = s.Key ?? string.Empty,
                Name = s.Name ?? s.Key ?? string.Empty,
                Value = s.Value ?? string.Empty,
            }).ToList() ?? new List<UnifiedStatEntry>();

            return new UnifiedPlayerStats { Grade = grade, Stats = stats };
        }

        public static UnifiedPlayerStats? MapHeyBoxStats(HeyBoxUserInfoResponse? resp)
        {
            var d = resp?.Data;
            if (d == null) return null;

            var p = d.PlayerInfo;
            var grade = p == null ? null : new UnifiedGradeInfo
            {
                GradeName = p.Level ?? string.Empty,
                GradeIcon = p.LevelImg ?? string.Empty,
                GradeScore = InferScoreFromRankName(p.Level),
                GradeLevel = p.Level ?? string.Empty,
            };

            var stats = d.Overview?.Select(o => new UnifiedStatEntry
            {
                // heyBox 用中文 desc 作 key —— StatsPageViewModel.FindStatValue 用中文关键字匹配，
                // 大部分场景仍能命中；不做英文 key 归一化避免维护双向表。
                Key = o.Desc ?? string.Empty,
                Name = o.Desc ?? string.Empty,
                Value = o.Value ?? string.Empty,
            }).ToList() ?? new List<UnifiedStatEntry>();

            return new UnifiedPlayerStats { Grade = grade, Stats = stats };
        }

        // === Seasons ===

        public static List<UnifiedSeason> MapSeasons(QuerySeasonsResponse? resp)
        {
            var list = resp?.Data;
            if (list == null) return new List<UnifiedSeason>();
            return list
                .Where(s => s.Code != null && s.Code > 0)
                .Select(s => new UnifiedSeason
                {
                    Code = s.Code ?? 0,
                    Name = s.Name ?? string.Empty,
                    HeyBoxSeasonName = s.HeyBoxSeasonName,
                })
                .ToList();
        }

        // === Recent battles ===

        public static List<UnifiedRecentBattleItem> MapMiniProgramRecent(GetRecentBattlesResponse? resp)
        {
            var list = resp?.Data?.List;
            if (list == null) return new List<UnifiedRecentBattleItem>();
            return list.Select(b => new UnifiedRecentBattleItem
            {
                BattleId = ((long)(b.BattleId ?? 0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Rank = (int)(b.Rank ?? 0),
                HeroIcon = b.Hero?.HeroIcon ?? string.Empty,
                HeroName = b.Hero?.HeroName ?? string.Empty,
                GameMode = (int)(b.Subtype ?? b.GameMode ?? 0),
                Kill = (int)(b.Kill ?? 0),
                Damage = (int)(b.Damage ?? 0),
                RoundRankScore = b.RoundRankScore ?? 0,
                BeginRankScore = b.BeginRankScore,
                BattleEndTimeMs = b.BattleEndTime ?? 0,
                Rating = b.Rating ?? string.Empty,
                HonorTitles = b.HonorTitles?.Select(MapHonor).ToArray()
                    ?? Array.Empty<UnifiedHonorTitle>(),
            }).ToList();
        }

        public static List<UnifiedRecentBattleItem> MapHeyBoxRecent(HeyBoxRecentBattlesResponse? resp)
        {
            var list = resp?.Data?.MatchList;
            if (list == null) return new List<UnifiedRecentBattleItem>();
            return list.Select(m => new UnifiedRecentBattleItem
            {
                BattleId = m.MatchId ?? string.Empty,
                Rank = (int)(m.Rank ?? 0),
                HeroIcon = m.HeroAvatar ?? string.Empty,
                HeroName = m.HeroName ?? string.Empty,
                // heyBox battleTid 与 miniProgram 的 subtype/gameMode 语义近似，但值域不同（如 5000000=天选单排）。
                // VM 层已通过 GameModeExtensions.FromBattleApiCode 兜底 ArgumentOutOfRangeException，
                // 不匹配时显示 Unknown(x)，属占位策略下的可接受行为。
                GameMode = ParseInt(m.BattleTid),
                Kill = (int)(m.KillTimes ?? 0),
                Damage = (int)(m.Damage ?? 0),
                RoundRankScore = ParseDouble(m.Rating),
                BeginRankScore = null,
                BattleEndTimeMs = (m.Time ?? 0) * 1000L,
                Rating = m.Grade ?? string.Empty,
                HonorTitles = Array.Empty<UnifiedHonorTitle>(),
            }).ToList();
        }

        // === Battle detail (personal + team + top5) ===

        public static UnifiedBattleDetail? MapMiniProgramBattleDetail(
            GetBattleDetailResponse? personal,
            GetTeamBattleDetailResponse? team,
            GetTop5BattleDetailResponse? top5)
        {
            var p = personal?.Data;
            if (p == null) return null;

            var personalView = new UnifiedPersonalDetail
            {
                HeroName = p.Hero?.HeroName ?? string.Empty,
                HeroIcon = p.Hero?.HeroIcon ?? string.Empty,
                RoleName = p.Role?.RoleName ?? string.Empty,
                Rank = (int)(p.Rank ?? 0),
                BattleEndTimeMs = p.BattleEndTime ?? 0,
                HonorTitles = p.HonorTitles?.Select(MapHonor).ToArray()
                    ?? Array.Empty<UnifiedHonorTitle>(),
                DataList = p.DataList?.Select(MapStatItem).ToArray()
                    ?? Array.Empty<UnifiedStatEntry>(),
                Weapons = p.Weapons?.Select(MapWeapon).ToArray()
                    ?? Array.Empty<UnifiedWeapon>(),
                SoulItems = p.SoulItems?.Select(MapSoul).ToArray()
                    ?? Array.Empty<UnifiedSoulItem>(),
                Armor = p.Armor == null ? null : new UnifiedArmor
                {
                    Icon = p.Armor.ArmorIcon ?? string.Empty,
                    Level = p.Armor.ArmorLevel ?? 0,
                },
            };

            var teamView = team?.Data?.Teammates?.Select(t => new UnifiedTeammate
            {
                HeroIcon = t.Hero?.HeroIcon ?? string.Empty,
                HeroName = t.Hero?.HeroName ?? string.Empty,
                RoleName = t.Role?.RoleName ?? string.Empty,
                IsMe = t.IsMe ?? false,
                Armor = t.Armor == null ? null : new UnifiedArmor
                {
                    Icon = t.Armor.ArmorIcon ?? string.Empty,
                    Level = t.Armor.ArmorLevel ?? 0,
                },
                Weapons = t.Weapons?.Select(MapWeapon).ToArray() ?? Array.Empty<UnifiedWeapon>(),
                SoulItems = t.SoulItems?.Select(MapSoul).ToArray() ?? Array.Empty<UnifiedSoulItem>(),
                DataList = t.DataList?.Select(MapStatItem).ToArray() ?? Array.Empty<UnifiedStatEntry>(),
            }).ToArray();

            var top5View = top5?.Data?.Top5?.Select(e => new UnifiedTop5Entry
            {
                Rank = (int)(e.Rank ?? 0),
                Members = e.Members?.Select(m => new UnifiedTop5Member
                {
                    HeroIcon = m.Hero?.HeroIcon ?? string.Empty,
                    HeroName = m.Hero?.HeroName ?? string.Empty,
                    RoleName = m.Role?.RoleName ?? string.Empty,
                    IsMe = m.IsMe ?? false,
                }).ToArray() ?? Array.Empty<UnifiedTop5Member>(),
            }).ToArray();

            return new UnifiedBattleDetail
            {
                Personal = personalView,
                Team = teamView,
                Top5 = top5View,
            };
        }

        public static UnifiedBattleDetail? MapHeyBoxBattleDetail(HeyBoxBattleDetailResponse? resp)
        {
            var d = resp?.Data;
            if (d == null) return null;

            var personalView = new UnifiedPersonalDetail
            {
                HeroName = string.Empty, // heyBox detail 不携带英雄名（有 heroId 无名字）
                HeroIcon = string.Empty,
                RoleName = d.Name ?? string.Empty,
                Rank = (int)(d.Rank ?? 0),
                BattleEndTimeMs = (d.Time ?? 0) * 1000L,
                HonorTitles = d.Tags?.Select(t => new UnifiedHonorTitle
                {
                    Icon = t.Img ?? string.Empty,
                    Name = t.Name ?? string.Empty,
                    Desc = t.Desc ?? string.Empty,
                }).ToArray() ?? Array.Empty<UnifiedHonorTitle>(),
                DataList = d.Data?.Select(s => new UnifiedStatEntry
                {
                    Key = s.Desc ?? string.Empty,
                    Name = s.Desc ?? string.Empty,
                    Value = s.Value ?? string.Empty,
                }).ToArray() ?? Array.Empty<UnifiedStatEntry>(),
                Weapons = d.WeaponList?.Select(w => new UnifiedWeapon
                {
                    Icon = w.Img ?? string.Empty,
                    Name = w.Name ?? string.Empty,
                    Level = 0, // heyBox 无武器等级字段
                    Kill = (int)(w.KillTimes ?? 0),
                    Damage = (int)(w.Damage ?? 0),
                    Percent = ParseDouble(w.Per),
                }).ToArray() ?? Array.Empty<UnifiedWeapon>(),
                SoulItems = d.SoulItemList?.Select(s => new UnifiedSoulItem
                {
                    Icon = s.Img ?? string.Empty,
                    Name = s.Name ?? string.Empty,
                    Level = 0,
                }).ToArray() ?? Array.Empty<UnifiedSoulItem>(),
                Armor = null, // heyBox 无护甲字段
            };

            return new UnifiedBattleDetail
            {
                Personal = personalView,
                Team = null,
                Top5 = null,
            };
        }

        // === Shared helpers ===

        private static UnifiedHonorTitle MapHonor(HonorTitleInfo h) => new()
        {
            Icon = h.HonorIcon ?? string.Empty,
            Name = h.HonorName ?? string.Empty,
            Desc = h.HonorDesc ?? string.Empty,
        };

        private static UnifiedStatEntry MapStatItem(StatItem s) => new()
        {
            Key = s.Key ?? string.Empty,
            Name = s.Name ?? s.Key ?? string.Empty,
            Value = s.Value ?? string.Empty,
        };

        private static UnifiedWeapon MapWeapon(WeaponInfo w) => new()
        {
            Icon = w.WeaponIcon ?? string.Empty,
            Name = w.WeaponName ?? string.Empty,
            Level = w.WeaponLevel ?? 0,
            Kill = (int)(w.Kill ?? 0),
            Damage = (int)(w.Damage ?? 0),
            Percent = (double)(w.Percent ?? 0),
        };

        private static UnifiedSoulItem MapSoul(SoulItemInfo s) => new()
        {
            Icon = s.SoulItemIcon ?? string.Empty,
            Name = s.SoulItemName ?? string.Empty,
            Level = s.SoulItemLevel ?? 0,
        };

        private static int ParseInt(string? s)
            => int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static double ParseDouble(string? s)
            => double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        /// <summary>
        /// 从 heyBox 段位名（"青铜Ⅴ" / "无相龙王" 等）反推 gradeScore 大段基线。
        /// heyBox 不返回具体分数，仅返段位名，用基线值让 UI 段位标签有内容可绑，
        /// 星级/进阶格无法计算 —— 属占位策略下的可接受降级。
        /// </summary>
        private static double InferScoreFromRankName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            // 大段基线，miniProgram 排位标注一致
            if (name.Contains("无量梵天")) return 7500;
            if (name.Contains("无相龙王")) return 6000;
            if (name.Contains("无双修罗")) return 5000;
            if (name.Contains("无间修罗")) return 4500;
            if (name.Contains("坠日")) return 4000;
            if (name.Contains("蚀月")) return 3500;
            if (name.Contains("陨星")) return 3000;
            if (name.Contains("铂金")) return 2500;
            if (name.Contains("黄金")) return 2000;
            if (name.Contains("白银")) return 1500;
            if (name.Contains("青铜")) return 1000;
            // 非排位段位（无间泰斗/御天尊者等）
            if (name.Contains("无间泰斗")) return 7000;
            if (name.Contains("御天尊者")) return 6500;
            if (name.Contains("劫虚圣主")) return 6000;
            if (name.Contains("穹苍魁首")) return 5500;
            if (name.Contains("日曜名宿")) return 5000;
            if (name.Contains("星月宗师")) return 4500;
            if (name.Contains("云霄武圣")) return 4000;
            if (name.Contains("绝顶高手")) return 3500;
            if (name.Contains("凡尘武师")) return 3000;
            return 0;
        }
    }
}
