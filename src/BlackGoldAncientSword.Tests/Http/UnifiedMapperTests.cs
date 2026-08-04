using System.Text.Json;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 覆盖 miniProgram / heyBox 两套响应体 → UnifiedXxx 的映射关键点：
    /// (1) DataSource 字符串 → 枚举；
    /// (2) miniProgram battleId(long) 与 heyBox matchId(string) 统一 BattleId 字符串；
    /// (3) heyBox time (秒) 转 BattleEndTimeMs (毫秒)；
    /// (4) heyBox overview[] 中文 desc 作为 UnifiedStatEntry.Key；
    /// (5) heyBox 段位名 → GradeScore 反推。
    /// </summary>
    public class UnifiedMapperTests
    {
        [Fact]
        public void MapSearch_HeyBox_DataSource_ParsedToEnum()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"roleIdSimple\":\"6118600130163\",\"roleName\":\"菜刀\",\"dataSource\":\"heyBox\",\"roleLevel\":0,\"levelName\":\"青铜Ⅴ\",\"levelImg\":\"https://x/1000.png\"}}";
            var resp = JsonSerializer.Deserialize<SearchRecordResponse>(json, NarakaApiClient.JsonOptions);
            var result = UnifiedMapper.MapSearch(resp);
            Assert.NotNull(result);
            Assert.Equal("6118600130163", result!.RoleIdSimple);
            Assert.Equal(DataSource.HeyBox, result.DataSource);
            Assert.Equal("菜刀", result.RoleName);
            Assert.Equal("青铜Ⅴ", result.LevelName);
        }

        [Fact]
        public void MapSearch_MiniProgram_DataSource_ParsedToEnum()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"roleIdSimple\":\"15949400120163\",\"roleName\":\"爱的供养丶\",\"dataSource\":\"miniProgram\",\"roleLevel\":439}}";
            var resp = JsonSerializer.Deserialize<SearchRecordResponse>(json, NarakaApiClient.JsonOptions);
            var result = UnifiedMapper.MapSearch(resp);
            Assert.NotNull(result);
            Assert.Equal(DataSource.MiniProgram, result!.DataSource);
            Assert.Equal(439, result.RoleLevel);
        }

        [Fact]
        public void MapSearch_ReturnsNull_WhenRoleIdSimpleMissing()
        {
            var json = "{\"code\":500,\"msg\":\"游戏昵称错误\",\"data\":null}";
            var resp = JsonSerializer.Deserialize<SearchRecordResponse>(json, NarakaApiClient.JsonOptions);
            var result = UnifiedMapper.MapSearch(resp);
            Assert.Null(result);
        }

        [Fact]
        public void MapMiniProgramRecent_BattleIdIntConvertedToString()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"list\":[{\"battleId\":265526241,\"rank\":11,\"kill\":12,\"damage\":29810,\"battleEndTime\":1782490539000,\"gameMode\":2,\"subtype\":2,\"roundRankScore\":6751,\"beginRankScore\":6721,\"rating\":\"C\",\"hero\":{\"heroId\":1000032,\"heroName\":\"巫真\",\"heroIcon\":\"https://x/1000032.png\"}}]}}";
            var resp = JsonSerializer.Deserialize<GetRecentBattlesResponse>(json, NarakaApiClient.JsonOptions);
            var items = UnifiedMapper.MapMiniProgramRecent(resp);
            Assert.Single(items);
            Assert.Equal("265526241", items[0].BattleId);
            Assert.Equal(11, items[0].Rank);
            Assert.Equal(12, items[0].Kill);
            Assert.Equal(1782490539000, items[0].BattleEndTimeMs);
            Assert.Equal(6721, items[0].BeginRankScore);
        }

        [Fact]
        public void MapHeyBoxRecent_MatchIdKeptAsString_TimeConvertedToMs()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"matchList\":[{\"rating\":\"6747\",\"scene\":\"2\",\"matchId\":\"liiu000000754200300163.1782913141\",\"battleTid\":\"5000001\",\"grade\":\"C\",\"playMode\":1,\"damage\":17469,\"rank\":13,\"playNum\":3,\"ratingDelta\":-4,\"killTimes\":3,\"time\":1782913141,\"mapName\":\"火罗国\",\"heroAvatar\":\"https://x/1000032.png\",\"heroId\":\"1000032\",\"heroName\":\"巫真\"}]}}";
            var resp = JsonSerializer.Deserialize<HeyBoxRecentBattlesResponse>(json, NarakaApiClient.JsonOptions);
            var items = UnifiedMapper.MapHeyBoxRecent(resp);
            Assert.Single(items);
            Assert.Equal("liiu000000754200300163.1782913141", items[0].BattleId);
            Assert.Equal(13, items[0].Rank);
            Assert.Equal(3, items[0].Kill);
            Assert.Equal(17469, items[0].Damage);
            Assert.Equal(1782913141L * 1000L, items[0].BattleEndTimeMs);
            Assert.Equal(6747, items[0].RoundRankScore);
            // heyBox 只给 ratingDelta（本局分差 -4），BeginRankScore 由 RoundRankScore 反推：6747 - (-4) = 6751
            Assert.Equal(6751, items[0].BeginRankScore);
            // battleTid=5000001（天选三排）归一化为 miniProgram battleApiCode 2（RankTrio），供 VM 统一消费
            Assert.Equal(2, items[0].GameMode);
        }

        [Fact]
        public void MapHeyBoxStats_OverviewMappedToStatsWithChineseKey()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"playerInfo\":{\"rating\":\"0\",\"name\":\"菜刀\",\"level\":\"青铜Ⅴ\",\"levelImg\":\"https://x/1000.png\",\"lv\":\"0\",\"avatar\":\"https://x/a.png\"},\"overview\":[{\"grade\":\"\",\"value\":\"42\",\"desc\":\"总场次\"},{\"grade\":\"A\",\"value\":\"4.8%\",\"desc\":\"前五率\"}],\"heroes\":[],\"weapons\":[]}}";
            var resp = JsonSerializer.Deserialize<HeyBoxUserInfoResponse>(json, NarakaApiClient.JsonOptions);
            var stats = UnifiedMapper.MapHeyBoxStats(resp);
            Assert.NotNull(stats);
            Assert.Equal(2, stats!.Stats.Count);
            Assert.Equal("总场次", stats.Stats[0].Key);
            Assert.Equal("总场次", stats.Stats[0].Name);
            Assert.Equal("42", stats.Stats[0].Value);
            Assert.Equal("前五率", stats.Stats[1].Key);
            Assert.Equal("4.8%", stats.Stats[1].Value);
            Assert.NotNull(stats.Grade);
            Assert.Equal("青铜Ⅴ", stats.Grade!.GradeName);
            // rating="0"（该模式无数据）→ 回退按段位名反推大段基线 青铜=1000
            Assert.Equal(1000, stats.Grade.GradeScore);
        }

        [Fact]
        public void MapHeyBoxStats_RealRatingUsedAsGradeScore_NotInferredFromName()
        {
            // playerInfo.rating="3629" 是真实排位分，必须直接采信（对齐网页端 蚀月Ⅳ 3629 分），
            // 而不是按段位名 "蚀月Ⅳ" 反推出大段基线 3500。
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"playerInfo\":{\"rating\":\"3629\",\"name\":\"爱的供养丶\",\"level\":\"蚀月Ⅳ\",\"levelImg\":\"https://x/3500.png\",\"lv\":\"439\",\"avatar\":\"https://x/a.png\"},\"overview\":[],\"heroes\":[],\"weapons\":[]}}";
            var resp = JsonSerializer.Deserialize<HeyBoxUserInfoResponse>(json, NarakaApiClient.JsonOptions);
            var stats = UnifiedMapper.MapHeyBoxStats(resp);
            Assert.NotNull(stats!.Grade);
            Assert.Equal("蚀月Ⅳ", stats.Grade!.GradeName);
            Assert.Equal(3629, stats.Grade.GradeScore);
        }

        [Fact]
        public void MapHeyBoxUser_UidTakenFromRoleIdSimple_SinceApiDoesNotReturnUid()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"playerInfo\":{\"rating\":\"0\",\"name\":\"菜刀\",\"level\":\"青铜Ⅴ\",\"levelImg\":\"\",\"lv\":\"0\",\"avatar\":\"\"},\"overview\":[],\"heroes\":[],\"weapons\":[]}}";
            var resp = JsonSerializer.Deserialize<HeyBoxUserInfoResponse>(json, NarakaApiClient.JsonOptions);
            var user = UnifiedMapper.MapHeyBoxUser(resp, "6118600130163");
            Assert.NotNull(user);
            Assert.Equal("菜刀", user!.RoleName);
            Assert.Equal("6118600130163", user.Uid);
            Assert.Null(user.CurrentSeasonId);
            Assert.Null(user.SoloRankScore);
        }

        [Fact]
        public void MapHeyBoxBattleDetail_TagsMappedToHonorTitles_TeamTop5Null()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"matchId\":\"pkjd000006118600130163.1629384576\",\"battleTid\":\"5000000\",\"rating\":\"1775\",\"weaponList\":[{\"killTimes\":0,\"per\":\"1.00\",\"damage\":974,\"img\":\"https://x/s.png\",\"name\":\"长枪\"}],\"name\":\"菜刀\",\"level\":\"白银Ⅲ\",\"levelImg\":\"\",\"ratingDelta\":\"34\",\"avatar\":\"\",\"time\":1629384576,\"mapName\":null,\"soulItemList\":[],\"playNum\":1,\"data\":[{\"grade\":null,\"value\":\"5.1k\",\"desc\":\"总伤害\"}],\"tags\":[{\"name\":\"飞天遁地\",\"img\":\"https://x/1005.png\",\"desc\":\"飞索移动400米\"}],\"playMode\":1,\"scene\":null,\"rank\":4,\"heroId\":null,\"grade\":\"A\"}}";
            var resp = JsonSerializer.Deserialize<HeyBoxBattleDetailResponse>(json, NarakaApiClient.JsonOptions);
            var detail = UnifiedMapper.MapHeyBoxBattleDetail(resp);
            Assert.NotNull(detail);
            Assert.Null(detail!.Team);
            Assert.Null(detail.Top5);
            Assert.Equal("菜刀", detail.Personal.RoleName);
            Assert.Equal(4, detail.Personal.Rank);
            Assert.Equal(1629384576L * 1000L, detail.Personal.BattleEndTimeMs);
            Assert.Single(detail.Personal.HonorTitles);
            Assert.Equal("飞天遁地", detail.Personal.HonorTitles[0].Name);
            Assert.Equal("飞索移动400米", detail.Personal.HonorTitles[0].Desc);
            Assert.Single(detail.Personal.DataList);
            Assert.Equal("总伤害", detail.Personal.DataList[0].Key);
            Assert.Equal("5.1k", detail.Personal.DataList[0].Value);
            Assert.Single(detail.Personal.Weapons);
            Assert.Equal(1.0, detail.Personal.Weapons[0].Percent);
        }

        [Fact]
        public void DataSourceFromApiString_UnknownStringFallsBackToMiniProgram()
        {
            Assert.Equal(DataSource.HeyBox, DataSourceExtensions.FromApiString("heyBox"));
            Assert.Equal(DataSource.HeyBox, DataSourceExtensions.FromApiString("heybox"));
            Assert.Equal(DataSource.MiniProgram, DataSourceExtensions.FromApiString("miniProgram"));
            Assert.Equal(DataSource.MiniProgram, DataSourceExtensions.FromApiString(null));
            Assert.Equal(DataSource.MiniProgram, DataSourceExtensions.FromApiString("garbage"));
        }
    }
}
