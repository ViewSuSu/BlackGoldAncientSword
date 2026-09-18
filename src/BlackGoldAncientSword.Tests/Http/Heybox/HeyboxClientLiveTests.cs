using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using BlackGoldAncientSword.Modules.UI.Stats.Services;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxClientLiveTests
    {
        private readonly ITestOutputHelper _output;

        public HeyboxClientLiveTests(ITestOutputHelper output) => _output = output;

        private static (PlayerStatsLoader Stats, BattleListLoader Battles) NewLoaders()
            => (new PlayerStatsLoader(new HeyboxHomeDataProvider(new HeyboxRequestCache()), new HeyboxRequestCache()), new BattleListLoader(new HeyboxRequestCache()));

        private async Task<(string RoleId, string Name)> RequireLocalPlayerAsync()
        {
            var prefs = new PlayerPrefsService();
            await prefs.LoadAsync();
            var roleId = prefs.Current.PlayerId;
            var name = prefs.Current.OriginalPlayerName;
            _output.WriteLine($"本机 role_id = {roleId}（昵称 {name}）");

            Assert.False(string.IsNullOrWhiteSpace(roleId),
                "本机取不到角色 role_id（player_prefs），Live 测试无法进行；请先在本机跑一次游戏。");
            Assert.False(string.IsNullOrWhiteSpace(name), "本机取不到玩家昵称（player_prefs）");
            return (roleId, name);
        }

        [Fact]
        public async Task Search_By_Local_Name_Picks_Local_RoleId()
        {
            var (roleId, name) = await RequireLocalPlayerAsync();
            var (stats, _) = NewLoaders();

            var byRoleId = await stats.SearchRoleByNameAsync(roleId, CancellationToken.None);
            _output.WriteLine($"[按 role_id 搜] 结果={(byRoleId is null ? "空（静默失败）" : byRoleId.RoleIdSimple)}");
            Assert.Null(byRoleId);

            var search = await stats.SearchLocalPlayerAsync(roleId, name, CancellationToken.None);
            Assert.NotNull(search);
            Assert.False(string.IsNullOrEmpty(search!.RoleIdSimple), "搜索必须给出 roleId（后续所有查询都靠它）");
            Assert.False(string.IsNullOrEmpty(search.Server), "搜索必须给出 server（后续所有查询都靠它）");
            _output.WriteLine($"roleId={search.RoleIdSimple}  server={search.Server}  " +
                              $"name={search.RoleName}  level={search.LevelName}");
            Assert.Equal(roleId, search.RoleIdSimple);
        }

        [Fact]
        public async Task PlayerHome_Should_Map_Profile_And_Seasons()
        {
            var session = HeyboxLiveSetup.LoadSession();
            if (session is null)
            {
                _output.WriteLine("没有本机登录态，跳过。先跑 Probe_WechatQrLogin_Native 扫码登录一次。");
                return;
            }

            var (roleId, name) = await RequireLocalPlayerAsync();
            var (stats, _) = NewLoaders();
            var ctx = await ResolveContextAsync(stats, roleId, name);

            HeyboxLiveSetup.ConfigureClient(session);
            HeyboxHomeResponse? home;
            try
            {
                home = await new HeyboxHomeDataProvider(new HeyboxRequestCache())
                    .GetAsync(ctx.RoleId, ctx.Server, season: null, battleTid: null, CancellationToken.None);
            }
            catch (NarakaApiException ex)
            {
                _output.WriteLine($"!! 未取得数据（msg={ex.Msg}）—— 契约验证未完成，别把绿当验过了。");
                return;
            }

            Assert.NotNull(home);
            if (!home!.IsSuccess)
            {
                _output.WriteLine($"!! status={home.Status} msg={home.Msg} —— 契约验证未完成，别把绿当验过了。");
                return;
            }

            var info = UnifiedMapper.MapPlayerInfo(home.Result);
            Assert.NotNull(info);
            Assert.False(string.IsNullOrEmpty(info!.RoleName), "玩家资料应有昵称");
            _output.WriteLine($"昵称={info.RoleName} 等级={info.RoleLevel} 头像={info.HeadIcon}");

            var seasons = UnifiedMapper.MapSeasons(home.Result?.Seasons);
            _output.WriteLine($"赛季数={seasons.Count}（最新={seasons.FirstOrDefault()?.Name}）");

            var playerStats = UnifiedMapper.MapSeasonSummary(home.Result);
            Assert.NotNull(playerStats);
            _output.WriteLine($"段位={playerStats!.Grade?.GradeName} 分={playerStats.Grade?.GradeScore} 指标数={playerStats.Stats.Count}");
            foreach (var s in playerStats.Stats)
                _output.WriteLine($"  {s.Name} = {s.Value}");
        }

        [Fact]
        public async Task MatchList_Then_Detail_Should_Map()
        {
            var session = HeyboxLiveSetup.LoadSession();
            if (session is null)
            {
                _output.WriteLine("没有本机登录态，跳过。");
                return;
            }

            var (roleId, name) = await RequireLocalPlayerAsync();
            var (statsLoader, battles) = NewLoaders();
            var ctx = await ResolveContextAsync(statsLoader, roleId, name);

            HeyboxLiveSetup.ConfigureClient(session);

            var list = await battles.FetchBattleListAsync(ctx, CancellationToken.None);
            if (list is null || list.Count == 0)
            {
                _output.WriteLine("对局列表为空（可能被限流，也可能该账号本赛季无对局），详情跳过。");
                return;
            }

            _output.WriteLine($"对局数={list.Count}");
            foreach (var b in list)
                _output.WriteLine($"  {b.BattleId} rank={b.Rank} mode={b.GameMode} kill={b.Kill} dmg={b.Damage} delta={b.ScoreDelta} time={b.BattleEndTimeMs}");

            var detail = await statsLoader.FetchBattleDetailAsync(ctx, list[0].BattleId, CancellationToken.None);
            if (detail is null)
            {
                _output.WriteLine("对局详情为空，跳过结构断言。");
                return;
            }

            var personal = detail.Personal;
            Assert.NotNull(personal);
            _output.WriteLine($"详情：玩家={personal!.RoleName} 名次={personal.Rank} " +
                              $"统计项={personal.DataList.Count} 武器={personal.Weapons.Count} " +
                              $"魂玉={personal.SoulItems.Count} 称号={personal.HonorTitles.Count}");
            var team = detail.Team ?? Array.Empty<UnifiedTeammate>();
            var top5 = detail.Top5 ?? Array.Empty<UnifiedTop5Entry>();
            _output.WriteLine($"      队伍={team.Count} 人，Top5 队伍={top5.Count}");

            foreach (var t in team)
                _output.WriteLine($"  队友 {t.RoleName} isMe={t.IsMe} 数据项={t.DataList.Count}");
        }

        private static async Task<PlayerSourceContext> ResolveContextAsync(PlayerStatsLoader stats, string roleId, string name)
        {
            var search = await stats.SearchLocalPlayerAsync(roleId, name, CancellationToken.None);
            Assert.NotNull(search);
            Assert.False(string.IsNullOrEmpty(search!.RoleIdSimple));
            return new PlayerSourceContext(search.RoleIdSimple, search.Server);
        }
    }
}
