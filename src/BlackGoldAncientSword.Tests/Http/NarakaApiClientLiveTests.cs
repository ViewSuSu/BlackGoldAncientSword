using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 打真实后端的联通性测试。用于验证 api-definitions.json 中的 baseUrl / path / query 参数
    /// 与线上 /app-api/ 前缀一致。任何一条 fail 说明客户端与线上契约漂移。
    /// 依赖 https://desktop.naraka.drivod.top 可访问，网络不通时会失败。
    /// </summary>
    [Trait("Category", "Live")]
    public class NarakaApiClientLiveTests
    {
        private const string MiniProgramRoleIdSimple = "15949400120163"; // 爱的供养丶
        private const string HeyBoxRoleIdSimple = "6118600130163"; // 菜刀

        [Fact]
        public async Task Search_ByName_ReturnsMiniProgramPlayer()
        {
            var resp = await NarakaApiClient.SearchRecordAsync("爱的供养丶", CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.Equal(MiniProgramRoleIdSimple, resp.Data!.RoleIdSimple);
            Assert.Equal("miniProgram", resp.Data.DataSource);
        }

        [Fact]
        public async Task Search_ByName_ReturnsHeyBoxPlayer()
        {
            var resp = await NarakaApiClient.SearchRecordAsync("菜刀", CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.Equal(HeyBoxRoleIdSimple, resp.Data!.RoleIdSimple);
            Assert.Equal("heyBox", resp.Data.DataSource);
        }

        [Fact]
        public async Task MiniProgram_GetUserInfo_ByRoleIdSimple()
        {
            var resp = await NarakaApiClient.GetUserInfoAsync(MiniProgramRoleIdSimple, CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.NotNull(resp.Data!.Role);
            Assert.Equal("爱的供养丶", resp.Data.Role!.RoleName);
        }

        [Fact]
        public async Task MiniProgram_GetRecentBattles()
        {
            var resp = await NarakaApiClient.GetRecentBattlesAsync(
                MiniProgramRoleIdSimple, gameMode: null, pageIndex: 1, pageSize: 20, ct: CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.NotNull(resp.Data!.List);
        }

        [Fact]
        public async Task QuerySeasons_Returns200()
        {
            var resp = await NarakaApiClient.QuerySeasonsAsync(CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.NotEmpty(resp.Data!);
        }

        [Fact]
        public async Task GetGameModes()
        {
            var resp = await NarakaApiClient.GetGameModesAsync(CancellationToken.None);
            Assert.NotNull(resp);
            Assert.NotNull(resp.Data);
            Assert.NotEmpty(resp.Data!);
        }

[Fact]
        public async Task HeyBox_GetUserInfo()
        {
            var resp = await NarakaApiClient.HeyBoxUserInfoAsync(HeyBoxRoleIdSimple, ct: CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.NotNull(resp.Data!.PlayerInfo);
            Assert.Equal("菜刀", resp.Data.PlayerInfo!.Name);
        }

        [Fact]
        public async Task HeyBox_GetRecentBattles()
        {
            var resp = await NarakaApiClient.HeyBoxRecentBattlesAsync(
                HeyBoxRoleIdSimple, pageIndex: 1, pageSize: 20, ct: CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code == 200 || resp.Code == 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
            Assert.NotNull(resp.Data!.MatchList);
        }

        // === Unified 链路端到端 ===

        [Fact]
        public async Task Unified_MiniProgramSearchAndUser()
        {
            var search = UnifiedMapper.MapSearch(
                await NarakaApiClient.SearchRecordAsync("爱的供养丶", CancellationToken.None));
            Assert.NotNull(search);
            Assert.Equal(DataSource.MiniProgram, search!.DataSource);

            var user = UnifiedMapper.MapMiniProgramUser(
                await NarakaApiClient.GetUserInfoAsync(search.RoleIdSimple, CancellationToken.None));
            Assert.NotNull(user);
            Assert.Equal("爱的供养丶", user!.RoleName);
            Assert.True(user.RoleLevel > 0, "RoleLevel should be > 0");
        }

        [Fact]
        public async Task Unified_HeyBoxSearchAndUser()
        {
            var search = UnifiedMapper.MapSearch(
                await NarakaApiClient.SearchRecordAsync("菜刀", CancellationToken.None));
            Assert.NotNull(search);
            Assert.Equal(DataSource.HeyBox, search!.DataSource);

            var user = UnifiedMapper.MapHeyBoxUser(
                await NarakaApiClient.HeyBoxUserInfoAsync(search.RoleIdSimple, ct: CancellationToken.None),
                search.RoleIdSimple);
            Assert.NotNull(user);
            Assert.Equal("菜刀", user!.RoleName);
        }
    }
}
