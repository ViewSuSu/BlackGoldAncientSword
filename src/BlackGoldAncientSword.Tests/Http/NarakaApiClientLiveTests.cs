using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using Moq;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 打真实后端的联通性测试。验证 api-definitions.json 中 unified 接口的 baseUrl / path / query
    /// 与线上 /app-api/record/unified/ 契约一致。任何一条 fail 说明客户端与线上契约漂移。
    /// 依赖 https://desktop.naraka.drivod.top 可访问，网络不通时会失败。
    /// 玩家相关用例一律用本机活跃账号的角色 ID 测试：走与主程序一致的现有逻辑
    /// （PlayerPrefsService 动态读取），不写死测试账号，拿不到 UID 直接失败，不回退名字搜索。
    /// 数据源一律用 search 返回的 source（unified 域实际为 heyBox），不写死 dashen。
    /// 通过 Collection 与其它 Live 测试串行，避免全局 NarakaApiClient.Configure 互相覆盖。
    /// </summary>
    [Trait("Category", "Live")]
    [Collection(UnifiedLiveCollection.Name)]
    public class NarakaApiClientLiveTests
    {
        private static readonly string RankSoloModeCode =
            GameMode.RankSolo.ToHeyBoxBattleTid().ToString(CultureInfo.InvariantCulture);

        private static bool _configured;

        /// <summary>挂 SignatureHandler + AuthTokenHandler 链，并从本地 auth.dat 恢复 token 到 state。只配置一次。</summary>
        private static void EnsureSignedAndAuthenticated()
        {
            if (_configured) return;
            var ticketProvider = new SignatureTicketProvider();
            var store = new DpapiAuthTokenStore();
            var token = store.Load();
            if (token == null)
                throw new InvalidOperationException("本地无有效 token，请先在 App 里登录（%APPDATA%\\BlackGoldAncientSword\\auth.dat）");

            var state = new AuthTokenState();
            state.Set(token);
            var refresher = new AuthTokenRefresher(ticketProvider);
            var challenge = new Mock<IAuthChallengeService>().Object;
            var handler = new SignatureHandler(ticketProvider)
            {
                InnerHandler = new AuthTokenHandler(state, store, refresher, challenge)
                {
                    InnerHandler = new HttpClientHandler()
                }
            };
            NarakaApiClient.Configure(handler);
            _configured = true;
        }

        /// <summary>
        /// 动态获取本机活跃账号的角色 ID：复用主程序现有逻辑（player_prefs 的 player_id，
        /// 权威信号为 Player.log 的 Login Success aid）。拿不到直接断言失败——测试必须用本机 UID。
        /// </summary>
        private static async Task<string> GetLocalRoleIdSimpleAsync()
        {
            var svc = new PlayerPrefsService();
            await svc.LoadAsync().ConfigureAwait(false);
            var id = svc.Current.PlayerId;
            Assert.False(
                string.IsNullOrWhiteSpace(id),
                "本机拿不到活跃账号角色 ID（player_prefs/Player.log），必须用本地 UID 测试，无法回退名字");
            return id!;
        }

        /// <summary>search 拿本机 UID 对应的统一数据源（不传 source，由后端决定），同时校验能搜到。</summary>
        private static async Task<(string roleId, string source)> SearchLocalRoleAsync()
        {
            var roleId = await GetLocalRoleIdSimpleAsync().ConfigureAwait(false);
            var resp = await NarakaApiClient.SearchRecordAsync(roleId, null, CancellationToken.None).ConfigureAwait(false);
            var search = UnifiedMapper.MapSearch(resp);
            Assert.NotNull(search);
            Assert.False(
                string.IsNullOrWhiteSpace(search!.RoleIdSimple),
                $"用本机 UID {roleId} 搜索未返回 roleIdSimple，msg=\"{resp.Msg}\"");
            return (roleId, search.DataSource.ToApiString());
        }

        [Fact]
        public async Task Search_ByLocalUid_ReturnsRoleId()
        {
            EnsureSignedAndAuthenticated();
            var resp = await NarakaApiClient.SearchRecordAsync(
                await GetLocalRoleIdSimpleAsync(), null, CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
            Assert.False(string.IsNullOrWhiteSpace(resp.Data?.RoleIdSimple), "search 未返回 roleIdSimple");
        }

        [Fact]
        public async Task GetPlayerProfile_ByLocalRoleId()
        {
            EnsureSignedAndAuthenticated();
            var (roleId, source) = await SearchLocalRoleAsync();

            var resp = await NarakaApiClient.GetPlayerProfileAsync(source, roleId, CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
        }

        [Fact]
        public async Task GetSeasonSummary_RankSolo()
        {
            EnsureSignedAndAuthenticated();
            var (roleId, source) = await SearchLocalRoleAsync();

            var resp = await NarakaApiClient.GetSeasonSummaryAsync(
                source, roleId, RankSoloModeCode, null, CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
        }

        [Fact]
        public async Task GetRecentMatches_AllModes()
        {
            EnsureSignedAndAuthenticated();
            var (roleId, source) = await SearchLocalRoleAsync();

            var resp = await NarakaApiClient.GetRecentMatchesAsync(
                source, roleId, modeCode: null, pageNo: 1, ct: CancellationToken.None);
            Assert.NotNull(resp);
            Assert.True(resp.Code is 200 or 0, $"expected success code, got {resp.Code}");
            Assert.NotNull(resp.Data);
        }

        [Fact]
        public async Task GetGameModes()
        {
            EnsureSignedAndAuthenticated();
            var resp = await NarakaApiClient.GetGameModesAsync(DataSource.HeyBox.ToApiString(), CancellationToken.None);
            Assert.NotNull(resp);
            Assert.NotNull(resp.Data);
        }
    }

    /// <summary>串行执行 Live 测试集合：全局 NarakaApiClient.Configure 只能被一个测试类持有。</summary>
    [CollectionDefinition(Name)]
    public sealed class UnifiedLiveCollection
    {
        public const string Name = "UnifiedLive";
    }
}
