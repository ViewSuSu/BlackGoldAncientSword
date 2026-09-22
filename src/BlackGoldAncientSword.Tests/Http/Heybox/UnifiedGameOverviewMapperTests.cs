using System;
using System.Text.Json;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    public class UnifiedGameOverviewMapperTests
    {

        private const string FullJson = """
        {"status":"ok","msg":"","result":{
          "appid":1203220,
          "name":"永劫无间",
          "name_en":"NARAKA: BLADEPOINT",
          "is_free":true,
          "score":"5.6",
          "follow_num":506257,
          "follow_num_str":"50.6w",
          "image":"https://img/cover.jpg",
          "appicon":"https://img/icon.webp",
          "comment_stats":{"star_1":"39.3760","score_comment":48047},
          "common_tags":[
            {"type":"steam_aggre","desc_list":["中文","多人","联网  \uF0DA"]},
            {"type":"simple_tag","desc":"动作"},
            {"type":"simple_tag","desc":"多人"}
          ],
          "screenshots":[
            {"type":"movie","thumbnail":"https://img/shot1.jpg","url":"https://v/1.m3u8"},
            {"type":"movie","thumbnail":"https://img/shot2.jpg","url":"https://v/2.m3u8"}
          ],
          "user_num":{"game_data":[
            {"desc":"当前在线","value":"1.8","hb_rich_text":{"attrs":[
              {"type":"text","text":"1.8"},{"type":"text","text":"万"}]}},
            {"desc":"15日在线趋势","value":"-","peak_values":[
              {"peak_value":"64891","time":1788796800},
              {"peak_value":"62156","time":1789920000}]},
            {"desc":"昨日峰值在线","value":"6.2","hb_rich_text":{"attrs":[
              {"type":"text","text":"6.2"},{"type":"text","text":"万"}]}},
            {"desc":"全语言好评率","value":"73%","hb_rich_text":{"attrs":[
              {"type":"image","image":"https://img/star.png"},{"type":"text","text":"73%"}]}},
            {"desc":"全球销量排行","value":"#163"},
            {"desc":"玩家数","value":"1275.6"},
            {"desc":"平均游戏时间","value":"197.5h"}
          ]}
        }}
        """;

        [Fact]
        public void Map_Should_Return_Null_For_Missing_Payload()
        {
            Assert.Null(UnifiedGameOverviewMapper.Map(null));
            Assert.Null(UnifiedGameOverviewMapper.Map(new HeyboxGameDetailResponse { Status = "ok" }));
        }

        [Fact]
        public void Map_Should_Read_Identity_And_Media()
        {
            var overview = UnifiedGameOverviewMapper.Map(Deserialize(FullJson));

            Assert.NotNull(overview);
            Assert.Equal("永劫无间", overview!.Name);
            Assert.Equal("NARAKA: BLADEPOINT", overview.NameEn);
            Assert.Equal("https://img/icon.webp", overview.IconUrl);
            Assert.Equal("https://img/cover.jpg", overview.CoverUrl);
            Assert.Equal("5.6", overview.Score);
            Assert.Equal("48,047", overview.ScoreCommentCount);
            Assert.Equal("50.6w", overview.FollowText);
            Assert.Equal(new[] { "https://img/shot1.jpg", "https://img/shot2.jpg" }, overview.MediaThumbnails);
        }

        [Fact]
        public void Map_Should_Join_Rich_Text_Attrs_For_Metric_Values()
        {
            var overview = UnifiedGameOverviewMapper.Map(Deserialize(FullJson));

            Assert.Equal("1.8万", overview!.OnlineNow);
            Assert.Equal("6.2万", overview.OnlinePeak);
            Assert.Equal("73%", overview.ApprovalRate);
        }

        [Fact]
        public void Map_Should_Fall_Back_To_Plain_Value_Without_Rich_Text()
        {
            var overview = UnifiedGameOverviewMapper.Map(Deserialize(FullJson));

            Assert.Equal("1275.6", overview!.PlayerCount);
            Assert.Equal("#163", overview.SalesRank);
            Assert.Equal("197.5h", overview.AvgPlayTime);
        }

        [Fact]
        public void Map_Should_Not_Mistake_Trend_Entry_For_Online_Metric()
        {
            var overview = UnifiedGameOverviewMapper.Map(Deserialize(FullJson));

            Assert.NotEqual("-", overview!.OnlineNow);
        }

        [Fact]
        public void Map_Should_Parse_Trend_Points_As_Utc8_Dates()
        {
            var overview = UnifiedGameOverviewMapper.Map(Deserialize(FullJson));

            Assert.Equal(2, overview!.OnlineTrend.Count);
            Assert.Equal(new DateTime(2026, 9, 8), overview.OnlineTrend[0].Date);
            Assert.Equal(64891, overview.OnlineTrend[0].Peak);
            Assert.Equal(new DateTime(2026, 9, 21), overview.OnlineTrend[1].Date);
            Assert.Equal(62156, overview.OnlineTrend[1].Peak);
        }

        [Fact]
        public void Map_Should_Clean_And_Deduplicate_Tags()
        {
            var overview = UnifiedGameOverviewMapper.Map(Deserialize(FullJson));

            Assert.Equal(new[] { "中文", "多人", "联网", "动作" }, overview!.Tags);
        }

        [Fact]
        public void Map_Should_Flag_Content_Availability()
        {
            Assert.True(UnifiedGameOverviewMapper.Map(Deserialize(FullJson))!.HasContent);
            Assert.False(UnifiedGameOverviewMapper.Map(Deserialize("""{"status":"ok","result":{}}"""))!.HasContent);
        }

        private static HeyboxGameDetailResponse Deserialize(string json)
            => JsonSerializer.Deserialize<HeyboxGameDetailResponse>(json, NarakaApiClient.JsonOptions)!;
    }
}
