using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    public class HeyboxMappingTests
    {

        [Fact]
        public void MapSearch_Should_Pick_RoleId_Server_And_Name_By_Cell_Type()
        {
            var resp = Deserialize<HeyboxSearchResponse>(SearchJson);

            var result = UnifiedMapper.MapSearch(resp);

            Assert.NotNull(result);
            Assert.Equal("uipe000001677200140163", result!.RoleIdSimple);
            Assert.Equal("163", result.Server);
            Assert.Equal("小窗", result.RoleName);
            Assert.Equal("https://img/avatar.png", result.Avatar);
            Assert.Equal("白银Ⅳ", result.LevelName);
            Assert.Equal("https://img/rank.png", result.LevelImg);
        }

        [Fact]
        public void MapSearch_Should_Carry_Remaining_Cells_As_Fields()
        {
            var resp = Deserialize<HeyboxSearchResponse>(SearchJson);

            var result = UnifiedMapper.MapSearch(resp);

            var field = Assert.Single(result!.Fields);
            Assert.Equal(string.Empty, field.Title);
            Assert.Equal("1600", field.Text);
        }

        [Fact]
        public void MapSearch_Should_Label_Extra_Fields_With_Header_Title()
        {
            const string json = """
            {"status":"ok","msg":"","result":{
              "header":[
                {"text":"斗士","type":"user_info","dw":0},
                {"text":"境界","type":"icon_text","dw":70},
                {"text":"积分","type":"text","dw":60}],
              "player_list":[{
                "game_id":"uipe000001677200140163","ext":"163",
                "column_list":[
                  {"type":"user_info","img":"https://img/avatar.png","text":"小窗"},
                  {"type":"icon_text","img":"https://img/rank.png","text":"白银Ⅳ"},
                  {"type":"text","text":"1600","value":"1600","sub_text":"上赛季"}
                ]}]}}
            """;

            var result = UnifiedMapper.MapSearch(Deserialize<HeyboxSearchResponse>(json));

            var field = Assert.Single(result!.Fields);
            Assert.Equal("积分", field.Title);
            Assert.Equal("1600 上赛季", field.Text);
        }

        [Fact]
        public void MapSearch_Should_Return_Null_When_No_Player_Matched()
        {
            var resp = Deserialize<HeyboxSearchResponse>("{\"status\":\"ok\",\"msg\":\"\",\"result\":{\"player_list\":[]}}");

            Assert.Null(UnifiedMapper.MapSearch(resp));
        }

        [Fact]
        public void MapSearchList_Should_Return_Every_Player_In_Order()
        {
            var resp = Deserialize<HeyboxSearchResponse>(SearchListJson);

            var results = UnifiedMapper.MapSearchList(resp);

            Assert.Equal(2, results.Count);
            Assert.Equal("uipe000001677200140163", results[0].RoleIdSimple);
            Assert.Equal("小窗", results[0].RoleName);
            Assert.Equal("l77c000015949400120163", results[1].RoleIdSimple);
            Assert.Equal("爱的供养丶", results[1].RoleName);
            Assert.Equal("163", results[1].Server);
        }

        [Fact]
        public void MapSearchList_Should_Skip_Players_Without_RoleId()
        {
            var resp = Deserialize<HeyboxSearchResponse>(
                "{\"status\":\"ok\",\"msg\":\"\",\"result\":{\"player_list\":[{\"game_id\":\"\",\"ext\":\"163\",\"column_list\":[]},{\"game_id\":\"uipe000001677200140163\",\"ext\":\"163\",\"column_list\":[]}]}}");

            var results = UnifiedMapper.MapSearchList(resp);

            Assert.Single(results);
            Assert.Equal("uipe000001677200140163", results[0].RoleIdSimple);
        }

        [Fact]
        public void MapSearchList_Should_Return_Empty_When_No_Player_Matched()
        {
            var resp = Deserialize<HeyboxSearchResponse>("{\"status\":\"ok\",\"msg\":\"\",\"result\":{\"player_list\":[]}}");

            Assert.Empty(UnifiedMapper.MapSearchList(resp));
        }

        [Fact]
        public void MapPlayerInfo_Should_Read_Name_Avatar_And_Level()
        {
            var home = HomeData();

            var info = UnifiedMapper.MapPlayerInfo(home);

            Assert.NotNull(info);
            Assert.Equal("小窗", info!.RoleName);
            Assert.Equal("https://img/avatar.png", info.HeadIcon);
            Assert.Equal("l77c000015949400120163", info.Uid);
            Assert.Equal(25, info.RoleLevel);
        }

        [Fact]
        public void MapSeasonSummary_Should_Read_Grade_And_Overview_Metrics()
        {
            var home = HomeData();

            var stats = UnifiedMapper.MapSeasonSummary(home);

            Assert.NotNull(stats);
            Assert.Equal("白银Ⅳ", stats!.Grade!.GradeName);
            Assert.Equal("https://img/rank.png", stats.Grade.GradeIcon);
            Assert.Equal(1600, stats.Grade.GradeScore);
            Assert.Equal(3, stats.Stats.Count);
            Assert.Equal("场均伤害", stats.Stats[0].Name);
            Assert.Equal("1024", stats.Stats[0].Value);
            Assert.Equal(string.Empty, stats.Stats[0].Grade);
            Assert.Equal("S", stats.Stats[2].Grade);
        }

        [Fact]
        public void MapSeasonSummary_Should_Read_Ability_Score_Roster_And_Recent_Ranks()
        {
            var home = HomeData();

            var stats = UnifiedMapper.MapSeasonSummary(home);

            Assert.NotNull(stats);
            var s = stats!;

            Assert.Equal(84.5, s.CompositeScore!.Value);
            Assert.Equal("A", s.ScoreGrade);
            Assert.Equal(6, s.ScoreList.Count);
            Assert.Equal("伤害量", s.ScoreList[0].Name);
            Assert.Equal(96.7, s.ScoreList[0].Score);
            Assert.Equal(84.5, s.ScoreList[5].Score);
            Assert.Equal(3, s.ScoreStats.Count);
            Assert.Equal("夺冠率", s.ScoreStats[0].Name);
            Assert.Equal("8.3%", s.ScoreStats[0].Value);
            Assert.Equal("S", s.ScoreStats[0].Grade);
            Assert.Equal("1.5", s.ScoreStats[2].Value);
            Assert.Equal(string.Empty, s.ScoreStats[2].Grade);

            Assert.Equal(13.4, s.RecentAvgRank);
            Assert.Equal(2, s.RecentRanks.Count);
            Assert.Equal("m1", s.RecentRanks[0].MatchId);
            Assert.Equal(1, s.RecentRanks[0].Rank);
            Assert.Equal(20, s.RecentRanks[1].Rank);

            var hero = Assert.Single(s.Heroes);
            Assert.Equal("1000026", hero.HeroId);
            Assert.Equal("张起灵", hero.Name);
            Assert.Equal(2688, hero.GameCount);
            Assert.Equal(12677, hero.AvgDamage);
            Assert.Equal("7.6%", hero.ChampionRate);
            Assert.Equal(64.7, hero.UsePercent);
            Assert.Equal(2.8, hero.Kd);
            Assert.Equal(1.0, hero.Percent);

            var weapon = Assert.Single(s.Weapons);
            Assert.Equal(2347, weapon.GameCount);
            Assert.Equal(2347, weapon.Round);
            Assert.Equal(1.0, weapon.Percent);
            Assert.Equal("2.8k", weapon.AvgDamage);
            Assert.Equal(2310, weapon.KillTimes);
            Assert.Equal(37522, weapon.MaxWeaponDamage);
            Assert.Equal(15, weapon.MaxKill);
        }

        [Fact]
        public void MapSeasonSummary_Should_Leave_Ability_Score_Null_When_ScoreInfo_Missing()
        {
            var home = Deserialize<HeyboxHomeResponse>(
                "{\"status\":\"ok\",\"msg\":\"\",\"result\":{\"player_info\":{\"level\":\"蚀月Ⅳ\",\"rating\":\"3629\"}}}");

            var stats = UnifiedMapper.MapSeasonSummary(home!.Result);

            Assert.NotNull(stats);
            Assert.Null(stats!.CompositeScore);
            Assert.Equal(string.Empty, stats.ScoreGrade);
            Assert.Empty(stats.ScoreList);
            Assert.Empty(stats.ScoreStats);
            Assert.Empty(stats.Heroes);
            Assert.Empty(stats.Weapons);
            Assert.Empty(stats.RecentRanks);
            Assert.Equal(0, stats.RecentAvgRank);
        }

        [Fact]
        public void MapSeasonSummary_Should_Treat_None_Rating_As_Zero()
        {
            var home = Deserialize<HeyboxHomeResponse>(
                "{\"status\":\"ok\",\"msg\":\"\",\"result\":{\"battle_tid\":\"5000000\","
                + "\"player_info\":{\"level\":\"\",\"rating\":\"None\"}}}");

            var stats = UnifiedMapper.MapSeasonSummary(home!.Result);

            Assert.NotNull(stats);
            Assert.Equal(0, stats!.Grade!.GradeScore);
        }

        [Fact]
        public void MapSeasons_Should_Keep_Key_As_SeasonKey_And_Value_As_Name()
        {
            var home = HomeData();

            var seasons = UnifiedMapper.MapSeasons(home!.Seasons);

            Assert.Equal(2, seasons.Count);
            Assert.Equal("pre-01", seasons[0].SeasonKey);
            Assert.Equal("全部", seasons[0].Name);
            Assert.Equal("qianji", seasons[1].SeasonKey);
        }

        [Fact]
        public void MapRecentMatches_Should_Derive_Mode_From_BattleTid()
        {
            var matches = new List<HeyboxMatchItem>
            {
                new() { MatchId = "m1", Rank = 1, BattleTid = "5000001", Rating = "3000", RatingDelta = "35", Time = 1789636970, KillTimes = 7, Damage = 12345, HeroAvatar = "https://img/hero.png", HeroId = "1000026", MapName = "龙隐洞天", PlayNum = 3, Grade = "S" },
            };

            var list = UnifiedMapper.MapRecentMatches(matches);

            var b = Assert.Single(list);
            Assert.Equal(2, b.GameMode);
            Assert.Equal("rank", b.ModeCategory);
            Assert.Equal(3, b.ModeTeamSize);
            Assert.Equal(3000, b.RoundRankScore);
            Assert.Equal(35, b.ScoreDelta);
            Assert.Equal(1789636970_000, b.BattleEndTimeMs);
            Assert.Equal("S", b.Rating);
            Assert.Equal("m1", b.BattleId);
            Assert.Equal("龙隐洞天", b.MapName);
            Assert.Equal(3, b.PlayNum);
            Assert.Equal("1000026", b.HeroId);
        }

        [Fact]
        public void MapRecentMatches_Should_Leave_Map_And_Hero_Empty_When_Absent()
        {
            var matches = new List<HeyboxMatchItem> { new() { MatchId = "m3", BattleTid = "5000001" } };

            var b = Assert.Single(UnifiedMapper.MapRecentMatches(matches));

            Assert.Equal(string.Empty, b.MapName);
            Assert.Equal(string.Empty, b.HeroId);
            Assert.Equal(0, b.PlayNum);
        }

        [Fact]
        public void MapRecentMatches_Should_Treat_Dash_Rating_And_Missing_BattleTid_As_Zero()
        {
            var matches = new List<HeyboxMatchItem>
            {
                new() { MatchId = "m2", Rank = 5, Rating = "-", RatingDelta = "-10" },
            };

            var b = Assert.Single(UnifiedMapper.MapRecentMatches(matches));

            Assert.Equal(0, b.RoundRankScore);
            Assert.Equal(-10, b.ScoreDelta);
            Assert.Equal(0, b.GameMode);
            Assert.Null(b.ModeCategory);
            Assert.Equal(0, b.ModeTeamSize);
        }

        [Fact]
        public void MapMatchDetail_Should_Split_Personal_Team_And_Top5()
        {
            var resp = Deserialize<HeyboxMatchDetailResponse>(MatchDetailJson);

            var mapped = UnifiedMapper.MapMatchDetail(resp);
            Assert.NotNull(mapped);
            var detail = mapped!;

            var p = detail.Personal;
            Assert.NotNull(p);
            Assert.Equal("小窗", p.RoleName);
            Assert.Equal(1, p.Rank);
            Assert.Equal(1789636970_000, p.BattleEndTimeMs);
            Assert.Equal("龙隐洞天", p.MapName);
            Assert.Equal("https://img/bg.png", p.BackgroundImage);
            Assert.Equal(3629, p.RoundRankScore);
            Assert.Equal(39, p.ScoreDelta);
            Assert.Equal("蚀月Ⅳ", p.LevelName);
            Assert.Equal("https://img/level.png", p.LevelIcon);
            Assert.Equal(2, p.DataList.Count);
            Assert.Single(p.Weapons);
            Assert.Equal(0.42, p.Weapons[0].Percent);
            Assert.Equal(3, p.Weapons[0].Kill);
            Assert.Equal(5416, p.Weapons[0].Damage);
            Assert.Single(p.SoulItems);
            Assert.Single(p.HonorTitles);

            var team = detail.Team!;
            Assert.Equal(2, team.Count);
            Assert.True(team[0].IsMe);
            Assert.Equal("小窗", team[0].RoleName);
            Assert.False(team[1].IsMe);
            Assert.Equal(5, team[0].DataList.Count);

            var top5 = detail.Top5!;
            Assert.Equal(2, top5.Count);
            Assert.Equal(1, top5[0].Rank);
            Assert.Equal(2, top5[0].Members.Count);
            Assert.Equal("小窗", top5[0].Members[0].RoleName);
        }

        [Fact]
        public void MapMatchDetail_Should_Tolerate_Empty_Team_Data()
        {
            var resp = Deserialize<HeyboxMatchDetailResponse>(
                "{\"status\":\"ok\",\"msg\":\"\",\"result\":{\"match_id\":\"m2\",\"name\":\"小窗\",\"all_team\":[],"
                + "\"weapon_list\":[{\"name\":\"其他\",\"per\":\"0.19\",\"damage\":1292}]}}");

            var detail = UnifiedMapper.MapMatchDetail(resp);

            Assert.NotNull(detail);
            Assert.Empty(detail!.Team!);
            Assert.Empty(detail.Top5!);
            Assert.Equal(string.Empty, detail.Personal.MapName);
            Assert.Equal(string.Empty, detail.Personal.LevelName);
            Assert.Equal(0, detail.Personal.RoundRankScore);
            Assert.Equal(1292, Assert.Single(detail.Personal.Weapons).Damage);
            Assert.Equal(0, detail.Personal.Weapons[0].Kill);
        }

        [Fact]
        public void MapMatchDetail_Should_Prefer_AllTeam_Over_Legacy_AllPlayerData()
        {
            const string json = """
            {"status":"ok","msg":"","result":{
              "match_id":"m3","name":"小窗",
              "all_team":[{"rank":1,"players":[{"role_name":"新字段","self":true,"damage":100,"kill":1}]}],
              "all_player_data":[{"rank":1,"players":[{"role_name":"旧字段","self":true,"damage":200,"kill":2}]}]}}
            """;

            var detail = UnifiedMapper.MapMatchDetail(Deserialize<HeyboxMatchDetailResponse>(json));

            Assert.Equal("新字段", Assert.Single(detail!.Team!).RoleName);
        }

        private static HeyboxHomeData? HomeData()
        {
            var resp = Deserialize<HeyboxHomeResponse>(HomeJson);
            return resp!.Result;
        }

        private static T? Deserialize<T>(string json)
            => JsonSerializer.Deserialize<T>(json, NarakaApiClient.JsonOptions);

        private const string SearchJson = """
        {"status":"ok","msg":"","result":{
          "header":[{"text":"斗士","type":"user_info","dw":0},{"text":"境界","type":"icon_text","dw":70}],
          "player_list":[{
            "game_id":"uipe000001677200140163","ext":"163",
            "column_list":[
              {"type":"user_info","img":"https://img/avatar.png","text":"小窗"},
              {"type":"icon_text","img":"https://img/rank.png","text":"白银Ⅳ"},
              {"type":"text","text":"1600"}
            ]}]}}
        """;

        private const string SearchListJson = """
        {"status":"ok","msg":"","result":{
          "header":[{"text":"斗士","type":"user_info","dw":0},{"text":"境界","type":"icon_text","dw":70}],
          "player_list":[
            {"game_id":"uipe000001677200140163","ext":"163",
             "column_list":[
               {"type":"user_info","img":"https://img/avatar.png","text":"小窗"},
               {"type":"icon_text","img":"https://img/rank.png","text":"白银Ⅳ"}
             ]},
            {"game_id":"l77c000015949400120163","ext":"163",
             "column_list":[
               {"type":"user_info","img":"https://img/avatar2.png","text":"爱的供养丶"},
               {"type":"icon_text","img":"https://img/rank2.png","text":"蚀月Ⅳ"}
             ]}]}}
        """;

        private const string HomeJson = """
        {"status":"ok","msg":"","result":{
          "role_id":"l77c000015949400120163","server":"163","battle_tid":"5000000","season":"qianji",
          "player_info":{"avatar":"https://img/avatar.png","name":"小窗","lv":25,"level":"白银Ⅳ","level_img":"https://img/rank.png","rating":1600},
          "seasons":[{"key":"pre-01","value":"全部"},{"key":"qianji","value":"千机赛季"}],
          "mode":[{"key":"5000000","value":"天选单排"}],
          "recent_avg_rank":"13.4",
          "recent_ranks":[
            {"match_id":"m1","rank":1},
            {"match_id":"m2","rank":20}
          ],
          "overview":[
            {"desc":"场均伤害","value":1024,"grade":""},
            {"desc":"总场次","value":557,"grade":""},
            {"desc":"夺冠率","value":"4.9%","grade":"S"}
          ],
          "score_info":{
            "composite_score":"84.5","score_grade":"A",
            "score_list":[
              {"name":"伤害量","score":"96.7"},
              {"name":"KD","score":"91.5"},
              {"name":"恢复量","score":"96"},
              {"name":"存活时间","score":"4.8"},
              {"name":"排名","score":"99.3"},
              {"name":"综合评分","score":"84.5"}
            ],
            "stats":[
              {"desc":"夺冠率","value":"8.3%","grade":"S"},
              {"desc":"前五率","value":"30.3%","grade":"S"},
              {"desc":"KD","value":"1.5","grade":""}
            ]
          },
          "heroes":[
            {"hero_id":"1000026","name":"张起灵","img":"https://img/hero.png",
             "data":{"game_count":2688,"avg_damage":12677,"rank_1_rate":"7.6%","use_percent":"64.7","kd":"2.8","percent":"1.00"}}
          ],
          "weapons":[
            {"name":"双节棍","img":"https://img/weapon.png",
             "data":{"game_count":"2347","round":2347,"percent":"1.00","avg_damage":"2.8k",
                     "kill_times":"2310","max_weapon_damage":"37522","max_kill":"15"}}
          ]}}
        """;

        private const string MatchDetailJson = """
        {"status":"ok","msg":"","result":{
          "match_id":"m1","name":"小窗","avatar":"https://img/hero.png",
          "rank":1,"time":1789636970,"rating":"3629","rating_delta":"39",
          "damage":12345,"shock_count":3,
          "map_name":"龙隐洞天","bg_img":"https://img/bg.png","level":"蚀月Ⅳ","level_img":"https://img/level.png",
          "tags":[{"img":"https://img/t.png","name":"夺冠","desc":"本局第一"}],
          "data":[{"desc":"伤害","value":"12345"},{"desc":"振刀","value":"3"}],
          "weapon_list":[{"name":"双节棍","per":0.42,"img":"https://img/w.png","kill_times":3,"damage":5416}],
          "soul_item_list":[{"bg":"","img":"https://img/s.png","name":"魂玉甲"}],
          "all_team":[
            {"rank":1,"players":[
              {"role_name":"小窗","self":true,"damage":12345,"per":40,"cure":100,"kill":7,"shock":3,"role_id":"a","server_id":"163"},
              {"role_name":"队友A","self":false,"damage":9000,"per":30,"cure":50,"kill":4,"shock":1,"role_id":"b","server_id":"163"}
            ]},
            {"rank":2,"players":[{"role_name":"对手","self":false,"damage":8000,"per":25,"cure":0,"kill":2,"shock":0,"role_id":"c","server_id":"163"}]}
          ]}}
        """;
    }

    public class HeyboxSignatureHandlerTests
    {
        private const long FixedTime = 1789636970;
        private const string FixedNonce = "A56916C1B0DA57AD71204DE9B4FD9A49";

        [Fact]
        public async Task Send_Should_Attach_Envelope_Params_And_Session_Identity()
        {
            var state = new HeyboxSessionState();
            state.Set(HeyboxLoginState.FromSession(new HeyboxSession("99365688", "secret-pkey")));

            var (query, cookie) = await SendAsync(state, "/game/yjwj/home/data?role_id=abc&server=163");

            Assert.Equal("heybox", query["app"]);
            Assert.Equal("web", query["os_type"]);
            Assert.Equal("heybox", query["x_app"]);
            Assert.Equal("weboutapp", query["x_client_type"]);
            Assert.Equal("Windows", query["x_os_type"]);
            Assert.Equal("", query["x_client_version"]);
            Assert.Equal("999.0.4", query["version"]);

            Assert.Equal("163", query["server"]);
            Assert.Equal("abc", query["role_id"]);

            Assert.Equal("99365688", query["heybox_id"]);
            Assert.Equal("99365688", query["user_id"]);

            Assert.Contains("user_heybox_id=99365688", cookie);
            Assert.Contains("user_pkey=secret-pkey", cookie);

            Assert.Equal(FixedTime.ToString(), query["_time"]);
            Assert.Equal(FixedNonce, query["nonce"]);
        }

        [Fact]
        public async Task Send_Should_Sign_Only_The_Path()
        {
            var (q1, _) = await SendAsync(new HeyboxSessionState(), "/game/yjwj/home/data?role_id=abc");
            var (q2, _) = await SendAsync(new HeyboxSessionState(), "/game/yjwj/home/data?role_id=zzz&season=1");

            Assert.Equal("22PT390", q1["hkey"]);
            Assert.Equal(q1["hkey"], q2["hkey"]);
        }

        [Fact]
        public async Task Send_Should_Not_Send_Identity_Or_Cookie_When_Logged_Out()
        {
            var (query, cookie) = await SendAsync(new HeyboxSessionState(), "/game/player_search/do?q=x");

            Assert.False(query.ContainsKey("heybox_id"));
            Assert.False(query.ContainsKey("user_id"));
            Assert.Null(cookie);
            Assert.Equal("163", query["server"]);
        }

        private static async Task<(Dictionary<string, string> Query, string? Cookie)> SendAsync(
            IHeyboxSessionState state, string url)
        {
            var capture = new CapturingHandler();
            using var handler = new HeyboxSignatureHandler(state, () => FixedTime, () => FixedNonce)
            {
                InnerHandler = capture,
            };
            using var client = new HttpClient(handler);

            await client.GetAsync("https://api.xiaoheihe.cn" + url);

            var sent = capture.LastRequest!;
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in sent.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                query[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }

            var cookie = sent.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : null;

            return (query, cookie);
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"msg\":\"\"}"),
                });
            }
        }
    }
}
