using System;
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
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 用本地已登录 token + 本机活跃账号 UID 打真实后端的全端点验证。验证目标是：search 返回的 source
    /// 必须被后续 player/season/matches 等接口原样复用；modeCode 必须传 heyBox battleTid（如 5000001）
    /// 而非 miniProgram 编码（如 3）。每个端点独立 try/catch 输出 code/msg，任一个失败不影响其它端点结果。
    /// 玩家一律用 PlayerPrefsService 现有逻辑动态获取的本机 UID，不写死名字、无名字回退；拿不到 UID 直接失败。
    /// 依赖 %APPDATA%\BlackGoldAncientSword\auth.dat（App 登录成功后落盘）。
    /// 通过 Collection 与其它 Live 测试串行，避免全局 NarakaApiClient.Configure 互相覆盖。
    /// </summary>
    [Trait("Category", "Live")]
    [Collection(UnifiedLiveCollection.Name)]
    public class UnifiedRecordLiveTests
    {
        private readonly ITestOutputHelper _output;
        public UnifiedRecordLiveTests(ITestOutputHelper output) => _output = output;

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

        /// <summary>复用主程序现有逻辑动态获取本机活跃账号角色 ID；拿不到直接失败，不回退名字。</summary>
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

        [Fact]
        public async Task Unified_AllEndpoints_LocalUser()
        {
            EnsureSignedAndAuthenticated();
            var ct = CancellationToken.None;

            var roleId = await GetLocalRoleIdSimpleAsync();

            // 1) search：不传 source，验证后端返回的 source
            var search = await SearchAsync(roleId, ct);
            if (search == null) return;
            var source = search.DataSource.ToApiString();
            _output.WriteLine($"[search] roleId={roleId} source={source}");

            // 2) player：用 search 返回的 source（修复后行为）
            await ProfileAsync("player(source=返回source)", source, roleId, ct);

            // 3) season：用返回 source + heyBox battleTid（修复后行为）
            var rankTrioTid = GameMode.RankTrio.ToHeyBoxBattleTid().ToString();
            await SeasonAsync("season(source=返回source, modeCode=5000001)", source, roleId, rankTrioTid, ct);

            // 3.5) matches 对比：heyBox 源 vs dashen 源的 honorTitles 返回情况
            await MatchesHonorCompareAsync(source, roleId, ct);

            // 4) matches：用返回 source 查最近对局，拿 detailKey 继续查详情
            var detailKey = await MatchesAsync(source, roleId, ct);
            if (detailKey != null)
            {
                await MatchDetailAsync(source, roleId, detailKey, ct);
                await MatchTeamAsync(source, roleId, detailKey, ct);
                await MatchTop5Async(source, roleId, detailKey, ct);
            }

            // 5) modes：不传 source，后端默认 dashen（与网页端对齐）。
            await ModesAsync(ct);
        }

        private async Task<UnifiedSearchResult?> SearchAsync(string roleId, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.SearchRecordAsync(roleId, null, ct);
                _output.WriteLine($"[search] code={resp.Code} msg=\"{resp.Msg}\" data={resp.Data?.RoleIdSimple} src={resp.Data?.Source}");
                return UnifiedMapper.MapSearch(resp);
            }
            catch (Exception ex) { _output.WriteLine($"[search] ERR {ex.Message}"); return null; }
        }

        private async Task ProfileAsync(string label, string source, string roleId, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetPlayerProfileAsync(source, roleId, ct);
                _output.WriteLine($"[{label}] code={resp.Code} msg=\"{resp.Msg}\" name={resp.Data?.DisplayName} lv={resp.Data?.Level}");
            }
            catch (Exception ex) { _output.WriteLine($"[{label}] ERR {ex.Message}"); }
        }

        private async Task SeasonAsync(string label, string source, string roleId, string modeCode, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetSeasonSummaryAsync(source, roleId, modeCode, null, ct);
                var rankName = resp.Data?.Rank?.Name;
                _output.WriteLine($"[{label}] code={resp.Code} msg=\"{resp.Msg}\" rank={rankName} season={resp.Data?.SeasonCode}");
            }
            catch (Exception ex) { _output.WriteLine($"[{label}] ERR {ex.Message}"); }
        }

        private async Task<string?> MatchesAsync(string source, string roleId, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetRecentMatchesAsync(source, roleId, modeCode: null, pageNo: 1, ct: ct);
                var records = resp.Data?.Records;
                var count = records?.Count ?? 0;
                var firstKey = count > 0 ? records![0].DetailKey : null;
                var firstMode = count > 0 ? records![0].Mode?.Name : null;
                _output.WriteLine($"[matches] code={resp.Code} msg=\"{resp.Msg}\" count={count} hasMore={resp.Data?.HasMore} detailKey={firstKey} mode={firstMode}");
                return firstKey;
            }
            catch (Exception ex) { _output.WriteLine($"[matches] ERR {ex.Message}"); return null; }
        }

        /// <summary>对比 heyBox 与 dashen 源下 matches 的 honorTitles 返回情况。</summary>
        private async Task MatchesHonorCompareAsync(string source, string roleId, CancellationToken ct)
        {
            // heyBox 源（当前 search 返回的 source）
            await LogHonorCountsAsync($"matches honor [{source}]", source, roleId, ct);
            // dashen 源对比
            await LogHonorCountsAsync("matches honor [dashen]", "dashen", roleId, ct);
        }

        private async Task LogHonorCountsAsync(string label, string source, string roleId, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetRecentMatchesAsync(source, roleId, modeCode: null, pageNo: 1, ct: ct);
                var records = resp.Data?.Records;
                var count = records?.Count ?? 0;
                var honorCounts = records?.Take(10).Select(r => r.HonorTitles?.Count ?? 0).ToList();
                _output.WriteLine($"[{label}] code={resp.Code} msg=\"{resp.Msg}\" count={count} honorCounts(前10)={string.Join(",", honorCounts ?? new())}");
            }
            catch (Exception ex) { _output.WriteLine($"[{label}] ERR {ex.Message}"); }
        }

        private async Task MatchDetailAsync(string source, string roleId, string detailKey, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetMatchDetailAsync(source, roleId, detailKey, ct);
                _output.WriteLine($"[match/detail] code={resp.Code} msg=\"{resp.Msg}\" mode={resp.Data?.Mode?.Name} hero={resp.Data?.Hero?.Name}");
            }
            catch (Exception ex) { _output.WriteLine($"[match/detail] ERR {ex.Message}"); }
        }

        private async Task MatchTeamAsync(string source, string roleId, string detailKey, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetMatchTeamAsync(source, roleId, detailKey, ct);
                _output.WriteLine($"[match/team] code={resp.Code} msg=\"{resp.Msg}\" members={resp.Data?.Count}");
            }
            catch (Exception ex) { _output.WriteLine($"[match/team] ERR {ex.Message}"); }
        }

        private async Task MatchTop5Async(string source, string roleId, string detailKey, CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetMatchTop5Async(source, roleId, detailKey, ct);
                _output.WriteLine($"[match/top5] code={resp.Code} msg=\"{resp.Msg}\" teams={resp.Data?.Count}");
            }
            catch (Exception ex) { _output.WriteLine($"[match/top5] ERR {ex.Message}"); }
        }

        private async Task ModesAsync(CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetGameModesAsync(null, ct);
                _output.WriteLine($"[modes(不传 source)] code={resp.Code} msg=\"{resp.Msg}\" count={resp.Data?.Count}");
            }
            catch (Exception ex) { _output.WriteLine($"[modes(不传 source)] ERR {ex.Message}"); }
        }
    }
}
