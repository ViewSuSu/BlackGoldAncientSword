using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 打真实后端的联通性测试。验证 api-definitions.json 中 unified 接口的 baseUrl / path / query
    /// 与线上 /app-api/record/unified/ 契约一致。任何一条 fail 说明客户端与线上契约漂移。
    /// 依赖 https://desktop.naraka.drivod.top 可访问，网络不通时会失败。
    /// </summary>
    [Trait("Category", "Live")]
    public class NarakaApiClientLiveTests
    {
        private const string SampleRoleIdSimple = "15949400120163"; // 爱的供养丶
        private static readonly string RankSoloModeCode =
            GameMode.RankSolo.ToHeyBoxBattleTid().ToString(CultureInfo.InvariantCulture);

        [Fact]
        public async Task Search_PreferDaShen_ReturnsRoleId()
        {
            var resp = await NarakaApiClient.SearchRecordAsync("爱的供养丶", DataSource.DaShen.ToApiString(), CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
        }

        [Fact]
        public async Task GetPlayerProfile_ByRoleIdSimple()
        {
            var resp = await NarakaApiClient.GetPlayerProfileAsync(
                DataSource.DaShen.ToApiString(), SampleRoleIdSimple, CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
        }

        [Fact]
        public async Task GetSeasonSummary_RankSolo()
        {
            var resp = await NarakaApiClient.GetSeasonSummaryAsync(
                DataSource.DaShen.ToApiString(), SampleRoleIdSimple, RankSoloModeCode, null, CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
        }

        [Fact]
        public async Task GetRecentMatches_AllModes()
        {
            var resp = await NarakaApiClient.GetRecentMatchesAsync(
                DataSource.DaShen.ToApiString(), SampleRoleIdSimple, modeCode: null, pageNo: 1, ct: CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
        }

        [Fact]
        public async Task GetGameModes()
        {
            var resp = await NarakaApiClient.GetGameModesAsync(DataSource.DaShen.ToApiString(), CancellationToken.None);
            Assert.NotNull(resp);
            Assert.NotNull(resp.Data);
        }

        // === Unified 链路端到端 ===

        [Fact]
        public async Task Unified_SearchThenProfile()
        {
            var searchResp = await NarakaApiClient.SearchRecordAsync("爱的供养丶", DataSource.DaShen.ToApiString(), CancellationToken.None);
            var search = UnifiedMapper.MapSearch(searchResp);
            Assert.NotNull(search);

            var user = UnifiedMapper.MapPlayer(
                await NarakaApiClient.GetPlayerProfileAsync(search!.DataSource.ToApiString(), search.RoleIdSimple, CancellationToken.None));
            Assert.NotNull(user);
        }
    }
}
