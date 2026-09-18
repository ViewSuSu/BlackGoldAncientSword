using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public static class UnifiedMapper
    {

        public static UnifiedSearchResult? MapSearch(HeyboxSearchResponse? resp) => MapSearch(resp, preferredRoleId: null);

        public static UnifiedSearchResult? MapSearch(HeyboxSearchResponse? resp, string? preferredRoleId)
        {
            var players = resp?.Result?.PlayerList;
            if (players is null || players.Count == 0) return null;

            var player =
                (!string.IsNullOrEmpty(preferredRoleId)
                    ? players.FirstOrDefault(p => string.Equals(p.GameId, preferredRoleId, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? players[0];
            if (string.IsNullOrEmpty(player.GameId)) return null;

            return MapSearchPlayer(player, resp?.Result?.Header);
        }

        public static List<UnifiedSearchResult> MapSearchList(HeyboxSearchResponse? resp)
        {
            var players = resp?.Result?.PlayerList;
            if (players is null || players.Count == 0) return new List<UnifiedSearchResult>();

            var results = new List<UnifiedSearchResult>(players.Count);
            foreach (var player in players)
            {
                if (string.IsNullOrEmpty(player.GameId)) continue;
                results.Add(MapSearchPlayer(player, resp?.Result?.Header));
            }
            return results;
        }

        private static UnifiedSearchResult MapSearchPlayer(
            HeyboxSearchPlayer player, List<HeyboxSearchColumn>? header)
        {
            var cells = player.ColumnList ?? new List<HeyboxSearchCell>();
            var info = cells.FirstOrDefault(c => string.Equals(c.Type, "user_info", StringComparison.Ordinal));
            var rank = cells.FirstOrDefault(c => string.Equals(c.Type, "icon_text", StringComparison.Ordinal));

            return new UnifiedSearchResult
            {
                RoleIdSimple = player.GameId!,
                Server = player.Ext ?? string.Empty,
                RoleName = info?.Text ?? string.Empty,
                Avatar = info?.Img ?? string.Empty,
                LevelName = rank?.Text ?? string.Empty,
                LevelImg = rank?.Img ?? string.Empty,
                DataSource = DataSource.HeyBox,
                Fields = MapSearchFields(cells, header),
            };
        }

        private static List<UnifiedSearchField> MapSearchFields(
            List<HeyboxSearchCell> cells, List<HeyboxSearchColumn>? header)
        {
            var fields = new List<UnifiedSearchField>(cells.Count);
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell is null) continue;
                if (string.Equals(cell.Type, "user_info", StringComparison.Ordinal)) continue;
                if (string.Equals(cell.Type, "icon_text", StringComparison.Ordinal)) continue;

                var text = JoinCellText(cell);
                if (text.Length == 0) continue;

                fields.Add(new UnifiedSearchField
                {
                    Title = header is not null && i < header.Count ? header[i]?.Text ?? string.Empty : string.Empty,
                    Text = text,
                });
            }
            return fields;
        }

        private static string JoinCellText(HeyboxSearchCell cell)
        {
            var parts = new[] { cell.Text, cell.SubText, cell.Value, cell.ArtText }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
        }

        public static UnifiedUserInfo? MapPlayerInfo(HeyboxHomeData? d)
        {
            if (d?.PlayerInfo == null) return null;

            return new UnifiedUserInfo
            {
                RoleName = d.PlayerInfo.Name ?? string.Empty,
                RoleLevel = UnifiedValueParser.ParseLooseNumber(d.PlayerInfo.Lv),
                Uid = d.RoleId ?? string.Empty,
                HeadIcon = d.PlayerInfo.Avatar ?? string.Empty,
                CurrentSeasonId = null,
                SoloRankScore = null,
                DuoRankScore = null,
                TrioRankScore = null,
            };
        }

        public static UnifiedPlayerStats? MapSeasonSummary(HeyboxHomeData? d)
        {
            if (d == null) return null;

            var info = d.PlayerInfo;
            var grade = info == null ? null : new UnifiedGradeInfo
            {
                GradeName = info.Level ?? string.Empty,
                GradeIcon = info.LevelImg ?? string.Empty,
                GradeScore = UnifiedValueParser.ParseLooseNumber(info.Rating),
                GradeLevel = string.Empty,
            };

            var stats = (d.Overview ?? new List<HeyboxOverviewEntry>())
                .Select(o => ToStatEntry(o.Desc, o.Value, o.Grade))
                .ToList();

            var scoreInfo = d.ScoreInfo;

            return new UnifiedPlayerStats
            {
                Grade = grade,
                Stats = stats,
                CompositeScore = string.IsNullOrEmpty(scoreInfo?.CompositeScore)
                    ? null
                    : UnifiedValueParser.ParseLooseNumber(scoreInfo!.CompositeScore),
                ScoreGrade = scoreInfo?.ScoreGrade ?? string.Empty,
                ScoreList = (scoreInfo?.ScoreList ?? new List<HeyboxScoreItem>())
                    .Select(i => new UnifiedScoreItem
                    {
                        Name = i.Name ?? string.Empty,
                        Score = UnifiedValueParser.ParseLooseNumber(i.Score),
                    })
                    .ToList(),
                ScoreStats = (scoreInfo?.Stats ?? new List<HeyboxScoreStat>())
                    .Select(s => ToStatEntry(s.Desc, s.Value, s.Grade))
                    .ToList(),
                RecentAvgRank = UnifiedValueParser.ParseLooseNumber(d.RecentAvgRank),
                RecentRanks = MapRecentRanks(d.RecentRanks),
                Heroes = UnifiedRosterMapper.MapHeroes(d.Heroes),
                Weapons = UnifiedRosterMapper.MapWeapons(d.Weapons),
            };
        }

        private static UnifiedStatEntry ToStatEntry(string? desc, string? value, string? grade) => new()
        {
            Key = desc ?? string.Empty,
            Name = desc ?? string.Empty,
            Value = value ?? string.Empty,
            Grade = grade ?? string.Empty,
        };

        private static IReadOnlyList<UnifiedRecentRank> MapRecentRanks(IEnumerable<HeyboxRecentRank>? ranks)
            => (ranks ?? Enumerable.Empty<HeyboxRecentRank>())
                .Select(r => new UnifiedRecentRank
                {
                    MatchId = r.MatchId ?? string.Empty,
                    Rank = (int)UnifiedValueParser.ParseLooseNumber(r.Rank),
                })
                .ToList();

        public static List<UnifiedSeason> MapSeasons(IEnumerable<HeyboxKeyValue>? seasons)
        {
            if (seasons == null) return new List<UnifiedSeason>();

            return seasons
                .Select(s => new UnifiedSeason
                {
                    SeasonKey = s.Key ?? string.Empty,
                    Name = s.Value ?? string.Empty,
                })
                .Where(s => !string.IsNullOrEmpty(s.SeasonKey) && !string.IsNullOrEmpty(s.Name))
                .ToList();
        }

        public static List<UnifiedRecentBattleItem> MapRecentMatches(IEnumerable<HeyboxMatchItem>? items)
        {
            if (items == null) return new List<UnifiedRecentBattleItem>();
            return items.Select(MapRecentBattle).ToList();
        }

        private static UnifiedRecentBattleItem MapRecentBattle(HeyboxMatchItem m)
        {
            var mode = ResolveMode(m.BattleTid);

            return new UnifiedRecentBattleItem
            {
                BattleId = m.MatchId ?? string.Empty,
                Rank = (int)UnifiedValueParser.ParseLooseNumber(m.Rank),
                HeroIcon = m.HeroAvatar ?? string.Empty,
                HeroId = m.HeroId ?? string.Empty,
                HeroName = string.Empty,
                MapName = m.MapName ?? string.Empty,
                PlayNum = (int)UnifiedValueParser.ParseLooseNumber(m.PlayNum),
                GameMode = mode.BattleApiCode,
                ModeCategory = mode.Category,
                ModeTeamSize = mode.TeamSize,
                ModeName = null,
                Kill = (int)UnifiedValueParser.ParseLooseNumber(m.KillTimes),
                Damage = (int)UnifiedValueParser.ParseLooseNumber(m.Damage),
                RoundRankScore = UnifiedValueParser.ParseLooseNumber(m.Rating),
                BeginRankScore = null,
                ScoreDelta = UnifiedValueParser.ParseLooseNumber(m.RatingDelta),
                BattleEndTimeMs = UnifiedValueParser.ToUnixMs(m.Time),
                Rating = m.Grade ?? string.Empty,
                RankName = m.Grade ?? string.Empty,
                HonorTitles = Array.Empty<UnifiedHonorTitle>(),
            };
        }

        public static UnifiedBattleDetail? MapMatchDetail(HeyboxMatchDetailResponse? resp)
        {
            var d = resp?.Result;
            if (d == null) return null;

            var personal = new UnifiedPersonalDetail
            {
                HeroName = string.Empty,
                HeroIcon = d.Avatar ?? string.Empty,
                RoleName = d.Name ?? string.Empty,
                ModeName = null,
                Rank = (int)UnifiedValueParser.ParseLooseNumber(d.Rank),
                BattleEndTimeMs = UnifiedValueParser.ToUnixMs(d.Time),
                MapName = d.MapName ?? string.Empty,
                BackgroundImage = d.BgImg ?? string.Empty,
                RoundRankScore = UnifiedValueParser.ParseLooseNumber(d.Rating),
                ScoreDelta = UnifiedValueParser.ParseLooseNumber(d.RatingDelta),
                LevelName = d.Level ?? string.Empty,
                LevelIcon = d.LevelImg ?? string.Empty,
                HonorTitles = (d.Tags ?? new List<HeyboxDetailTag>())
                    .Select(t => new UnifiedHonorTitle
                    {
                        Icon = t.Img ?? string.Empty,
                        Name = t.Name ?? string.Empty,
                        Desc = t.Desc ?? string.Empty,
                    }).ToArray(),
                DataList = (d.Data ?? new List<HeyboxDetailStat>())
                    .Select(s => ToStatEntry(s.Desc, s.Value, s.Grade))
                    .ToArray(),
                Weapons = (d.WeaponList ?? new List<HeyboxDetailWeapon>())
                    .Select(w => new UnifiedWeapon
                    {
                        Icon = w.Img ?? string.Empty,
                        Name = w.Name ?? string.Empty,
                        Level = 0,
                        Kill = (int)UnifiedValueParser.ParseLooseNumber(w.KillTimes),
                        Damage = (int)UnifiedValueParser.ParseLooseNumber(w.Damage),
                        Percent = w.Per ?? 0,
                    }).ToArray(),
                SoulItems = (d.SoulItemList ?? new List<HeyboxDetailSoulItem>())
                    .Select(s => new UnifiedSoulItem
                    {
                        Icon = s.Img ?? string.Empty,
                        Name = s.Name ?? string.Empty,
                        Level = 0,
                    }).ToArray(),
                Armor = null,
            };

            var teams = d.AllTeam is { Count: > 0 } ? d.AllTeam : d.AllPlayerData;
            var teamList = teams ?? new List<HeyboxTeamEntry>();
            var mine = teamList.FirstOrDefault();

            var teamView = mine?.Players?
                .Select(p => new UnifiedTeammate
                {
                    HeroIcon = string.Empty,
                    HeroName = string.Empty,
                    RoleName = p.RoleName ?? string.Empty,
                    IsMe = p.Self ?? false,
                    Armor = null,
                    Weapons = Array.Empty<UnifiedWeapon>(),
                    SoulItems = Array.Empty<UnifiedSoulItem>(),
                    DataList = MapTeamPlayerStats(p),
                }).ToArray() ?? Array.Empty<UnifiedTeammate>();

            var top5View = teamList
                .Where(t => ((int)UnifiedValueParser.ParseLooseNumber(t.Rank)) is > 0 and <= 5)
                .Select(t => new UnifiedTop5Entry
                {
                    Rank = (int)UnifiedValueParser.ParseLooseNumber(t.Rank),
                    Members = (t.Players ?? new List<HeyboxTeamPlayer>())
                        .Select(p => new UnifiedTop5Member
                        {
                            HeroIcon = string.Empty,
                            HeroName = string.Empty,
                            RoleName = p.RoleName ?? string.Empty,
                            IsMe = p.Self ?? false,
                        }).ToArray(),
                }).ToArray();

            return new UnifiedBattleDetail
            {
                Personal = personal,
                Team = teamView,
                Top5 = top5View,
            };
        }

        private static UnifiedStatEntry[] MapTeamPlayerStats(HeyboxTeamPlayer p)
        {
            var entries = new List<UnifiedStatEntry>(5);
            Add("伤害", p.Damage);
            Add("击杀", p.Kill);
            Add("振刀", p.Shock);
            Add("治疗", p.Cure);
            Add("伤害占比", p.Per);
            return entries.ToArray();

            void Add(string label, double? value)
            {
                if (value == null) return;
                entries.Add(new UnifiedStatEntry
                {
                    Key = label,
                    Name = label,
                    Value = UnifiedValueParser.FormatStatValue(value.Value),
                });
            }
        }

        private static (int BattleApiCode, string? Category, int TeamSize) ResolveMode(string? battleTid)
        {
            if (string.IsNullOrEmpty(battleTid)) return (0, null, 0);

            if (!int.TryParse(battleTid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tid))
                return (0, null, 0);

            try
            {
                var mode = GameModeExtensions.FromHeyBoxBattleTid(tid);
                return (mode.ToBattleApiCode(), ToCategoryString(mode.GetCategory()), (int)mode.GetTeamSize());
            }
            catch (ArgumentOutOfRangeException)
            {
                return (0, null, 0);
            }
        }

        private static string ToCategoryString(GameModeCategory category) => category switch
        {
            GameModeCategory.Rank => "rank",
            GameModeCategory.Match => "match",
            GameModeCategory.Tianren => "tianren",
            GameModeCategory.Fun => "fun",
            _ => string.Empty,
        };
    }
}
