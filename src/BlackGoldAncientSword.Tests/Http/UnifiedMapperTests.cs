using System.Text.Json;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 覆盖 unified 接口响应 → UnifiedXxx 的映射关键点：
    /// (1) search 的 source 字符串（含 dashen）→ 枚举；
    /// (2) matches 的 detailKey / mode.code(battleTidHeyBox) 归一化 / ISO-8601 时间 → 毫秒 / score.delta；
    /// (3) season 的 rank + metrics → UnifiedPlayerStats；
    /// (4) match 详情 personal/team/top5 合并。
    /// </summary>
    public class UnifiedMapperTests
    {
        [Fact]
        public void MapSearch_DaShen_SourceParsedToEnum()
        {
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{\"roleIdSimple\":\"6118600130163\",\"source\":\"dashen\"}}";
            var resp = JsonSerializer.Deserialize<SearchRecordResponse>(json, NarakaApiClient.JsonOptions);
            var result = UnifiedMapper.MapSearch(resp);
            Assert.NotNull(result);
            Assert.Equal("6118600130163", result!.RoleIdSimple);
            Assert.Equal(DataSource.DaShen, result.DataSource);
        }

        [Fact]
        public void MapSearch_HeyBox_SourceParsedToEnum()
        {
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{\"roleIdSimple\":\"15949400120163\",\"source\":\"heyBox\"}}";
            var resp = JsonSerializer.Deserialize<SearchRecordResponse>(json, NarakaApiClient.JsonOptions);
            var result = UnifiedMapper.MapSearch(resp);
            Assert.NotNull(result);
            Assert.Equal(DataSource.HeyBox, result!.DataSource);
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
        public void MapRecentMatches_DetailKeyKept_ModeCodeNormalized_IsoTimeToMs_DeltaUsed()
        {
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{\"hasMore\":true,\"records\":[{" +
                "\"detailKey\":\"abc.123\",\"occurredAt\":\"2026-06-01T12:30:00Z\"," +
                "\"mode\":{\"code\":\"5000001\",\"name\":\"天选三排\",\"category\":\"rank\",\"teamSize\":3}," +
                "\"hero\":{\"id\":\"1000032\",\"name\":\"巫真\",\"iconUrl\":\"https://x/h.png\"}," +
                "\"rank\":13,\"score\":{\"begin\":6751,\"end\":6747,\"delta\":-4}," +
                "\"evaluation\":{\"score\":80,\"level\":\"C\"}," +
                "\"kills\":3,\"damage\":17469,\"shockCount\":0,\"stats\":[]}]}}";
            var resp = JsonSerializer.Deserialize<GetRecentMatchesResponse>(json, NarakaApiClient.JsonOptions);
            var items = UnifiedMapper.MapRecentMatches(resp);
            Assert.Single(items);
            Assert.Equal("abc.123", items[0].BattleId);
            Assert.Equal(13, items[0].Rank);
            Assert.Equal(3, items[0].Kill);
            Assert.Equal(17469, items[0].Damage);
            // mode.code=5000001（天选三排）归一化为 battleApiCode 2（RankTrio）
            Assert.Equal(2, items[0].GameMode);
            // 后端 mode 对象原样保留，供 VM 与网页一致显示模式名
            Assert.Equal("天选三排", items[0].ModeName);
            Assert.Equal("rank", items[0].ModeCategory);
            Assert.Equal(3, items[0].ModeTeamSize);
            Assert.Equal(6747, items[0].RoundRankScore);
            Assert.Equal(6751, items[0].BeginRankScore);
            Assert.Equal(-4, items[0].ScoreDelta);
            Assert.Equal("C", items[0].RankName);
            // 2026-06-01T12:30:00Z → 毫秒
            Assert.Equal(1780317000000L, items[0].BattleEndTimeMs);
        }

        [Fact]
        public void MapRecentMatches_DaShenNullMode_ModeFieldsEmpty()
        {
            // dashen 源部分对局 mode 为 null（网页显示"未知模式"）；映射不得抛异常，mode 字段留空。
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{\"hasMore\":false,\"records\":[{" +
                "\"detailKey\":\"f66fc29658ccada6\",\"occurredAt\":\"2026-08-05T08:59:58Z\"," +
                "\"mode\":null,\"hero\":{\"id\":\"1000026\",\"name\":\"张起灵\",\"iconUrl\":\"https://x/h.png\"}," +
                "\"rank\":1,\"score\":null,\"evaluation\":{\"score\":0,\"level\":\"C\"}," +
                "\"kills\":0,\"damage\":0,\"shockCount\":0,\"stats\":[]}]}}";
            var resp = JsonSerializer.Deserialize<GetRecentMatchesResponse>(json, NarakaApiClient.JsonOptions);
            var items = UnifiedMapper.MapRecentMatches(resp);
            Assert.Single(items);
            Assert.Equal(0, items[0].GameMode);
            Assert.Null(items[0].ModeName);
            Assert.Null(items[0].ModeCategory);
            Assert.Equal(0, items[0].ModeTeamSize);
        }

        [Fact]
        public void MapSeasonSummary_RankAndMetricsMapped()
        {
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{" +
                "\"seasonCode\":\"S1\"," +
                "\"mode\":{\"code\":\"5000000\",\"name\":\"天选单排\",\"category\":\"rank\",\"teamSize\":1}," +
                "\"rank\":{\"name\":\"蚀月\",\"iconUrl\":\"https://x/3500.png\",\"score\":3629,\"level\":\"Ⅳ\"}," +
                // 真实后端：百分率的 value 已含 %（如 "4.8%"），unit 是语义标签（%/count/...），客户端不拼接。
                "\"metrics\":[{\"code\":\"round\",\"label\":\"总场次\",\"value\":\"42\",\"unit\":\"count\"}," +
                "{\"code\":\"top5_rate\",\"label\":\"前五率\",\"value\":\"4.8%\",\"unit\":\"%\"}]}}";
            var resp = JsonSerializer.Deserialize<GetSeasonSummaryResponse>(json, NarakaApiClient.JsonOptions);
            var stats = UnifiedMapper.MapSeasonSummary(resp);
            Assert.NotNull(stats);
            Assert.NotNull(stats!.Grade);
            Assert.Equal("蚀月", stats.Grade!.GradeName);
            Assert.Equal("Ⅳ", stats.Grade.GradeLevel);
            Assert.Equal(3629, stats.Grade.GradeScore);
            Assert.Equal(2, stats.Stats.Count);
            Assert.Equal("round", stats.Stats[0].Key);
            Assert.Equal("总场次", stats.Stats[0].Name);
            Assert.Equal("42", stats.Stats[0].Value);
            Assert.Equal("top5_rate", stats.Stats[1].Key);
            Assert.Equal("4.8%", stats.Stats[1].Value);
        }

        [Fact]
        public void MapMatchDetail_PersonalMapped_TeamAndTop5Merged()
        {
            var personalJson = "{\"code\":0,\"msg\":\"\",\"data\":{" +
                "\"detailKey\":\"abc.123\",\"occurredAt\":\"2026-06-01T12:30:00Z\"," +
                "\"mode\":{\"code\":\"5000000\",\"name\":\"天选单排\",\"category\":\"rank\",\"teamSize\":1}," +
                "\"hero\":{\"id\":\"1000032\",\"name\":\"巫真\",\"iconUrl\":\"https://x/h.png\"}," +
                "\"rank\":4,\"score\":{\"begin\":1000,\"end\":1034,\"delta\":34}," +
                "\"evaluation\":{\"score\":90,\"level\":\"A\"},\"kills\":5,\"damage\":9740,\"shockCount\":1," +
                "\"stats\":[{\"code\":\"damage\",\"name\":\"总伤害\",\"value\":9740,\"unit\":\"\"}]," +
                "\"player\":{\"source\":\"dashen\",\"roleIdSimple\":\"r1\",\"displayName\":\"菜刀\",\"avatarUrl\":\"\",\"level\":10}," +
                "\"weapons\":[{\"id\":\"w1\",\"name\":\"长枪\",\"iconUrl\":\"https://x/s.png\",\"level\":1,\"kills\":2,\"damage\":974,\"percent\":1.0}]," +
                "\"soulItems\":[],\"honorTitles\":[{\"id\":\"1005\",\"name\":\"飞天遁地\",\"iconUrl\":\"https://x/1005.png\",\"description\":\"飞索移动400米\"}]}}";
            var teamJson = "{\"code\":0,\"msg\":\"\",\"data\":[{" +
                "\"player\":{\"source\":\"dashen\",\"roleIdSimple\":\"r1\",\"displayName\":\"菜刀\",\"avatarUrl\":\"\",\"level\":10}," +
                "\"hero\":{\"id\":\"1000032\",\"name\":\"巫真\",\"iconUrl\":\"https://x/h.png\"},\"me\":true," +
                "\"score\":{\"begin\":1000,\"end\":1034,\"delta\":34},\"evaluation\":{\"score\":90,\"level\":\"A\"}," +
                "\"kills\":5,\"damage\":9740,\"shockCount\":1,\"stats\":[]," +
                "\"armor\":{\"id\":\"a1\",\"level\":2,\"iconUrl\":\"https://x/a.png\"}," +
                "\"weapons\":[],\"soulItems\":[],\"honorTitles\":[]}]}";
            var top5Json = "{\"code\":0,\"msg\":\"\",\"data\":[{\"rank\":1,\"members\":[{" +
                "\"displayName\":\"菜刀\",\"hero\":{\"id\":\"1000032\",\"name\":\"巫真\",\"iconUrl\":\"https://x/h.png\"}," +
                "\"me\":true,\"kills\":5,\"damage\":9740,\"healing\":0,\"survivalSeconds\":600}]}]}";

            var personal = JsonSerializer.Deserialize<GetMatchDetailResponse>(personalJson, NarakaApiClient.JsonOptions);
            var team = JsonSerializer.Deserialize<GetMatchTeamResponse>(teamJson, NarakaApiClient.JsonOptions);
            var top5 = JsonSerializer.Deserialize<GetMatchTop5Response>(top5Json, NarakaApiClient.JsonOptions);

            var detail = UnifiedMapper.MapMatchDetail(personal, team, top5);
            Assert.NotNull(detail);
            Assert.Equal("菜刀", detail!.Personal.RoleName);
            Assert.Equal("巫真", detail.Personal.HeroName);
            // 详情标题直接用后端完整 mode.name（与网页一致）
            Assert.Equal("天选单排", detail.Personal.ModeName);
            Assert.Equal(4, detail.Personal.Rank);
            Assert.Equal(1780317000000L, detail.Personal.BattleEndTimeMs);
            Assert.Single(detail.Personal.HonorTitles);
            Assert.Equal("飞天遁地", detail.Personal.HonorTitles[0].Name);
            Assert.Single(detail.Personal.Weapons);
            Assert.Equal(1.0, detail.Personal.Weapons[0].Percent);

            Assert.NotNull(detail.Team);
            Assert.Single(detail.Team!);
            Assert.True(detail.Team![0].IsMe);
            Assert.Equal(2, detail.Team[0].Armor!.Level);

            Assert.NotNull(detail.Top5);
            Assert.Single(detail.Top5!);
            Assert.Equal(1, detail.Top5![0].Rank);
            Assert.Single(detail.Top5[0].Members);
            Assert.True(detail.Top5[0].Members[0].IsMe);
        }

        [Fact]
        public void MapPlayer_ProfileMapped()
        {
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{\"source\":\"dashen\",\"roleIdSimple\":\"r1\"," +
                "\"roleId\":\"x\",\"displayName\":\"菜刀\",\"avatarUrl\":\"https://x/a.png\",\"level\":439}}";
            var resp = JsonSerializer.Deserialize<GetPlayerProfileResponse>(json, NarakaApiClient.JsonOptions);
            var user = UnifiedMapper.MapPlayer(resp);
            Assert.NotNull(user);
            Assert.Equal("菜刀", user!.RoleName);
            Assert.Equal("r1", user.Uid);
            Assert.Equal(439, user.RoleLevel);
        }

        [Fact]
        public void DataSourceFromApiString_MapsAllThreeSources_FallsBackToMiniProgram()
        {
            Assert.Equal(DataSource.HeyBox, DataSourceExtensions.FromApiString("heyBox"));
            Assert.Equal(DataSource.HeyBox, DataSourceExtensions.FromApiString("heybox"));
            Assert.Equal(DataSource.DaShen, DataSourceExtensions.FromApiString("dashen"));
            Assert.Equal(DataSource.DaShen, DataSourceExtensions.FromApiString("DaShen"));
            Assert.Equal(DataSource.MiniProgram, DataSourceExtensions.FromApiString("miniProgram"));
            Assert.Equal(DataSource.MiniProgram, DataSourceExtensions.FromApiString(null));
            Assert.Equal(DataSource.MiniProgram, DataSourceExtensions.FromApiString("garbage"));
        }

        [Fact]
        public void DataSourceToApiString_RoundTrips()
        {
            Assert.Equal("dashen", DataSource.DaShen.ToApiString());
            Assert.Equal("heyBox", DataSource.HeyBox.ToApiString());
            Assert.Equal("miniProgram", DataSource.MiniProgram.ToApiString());
        }
    }
}
