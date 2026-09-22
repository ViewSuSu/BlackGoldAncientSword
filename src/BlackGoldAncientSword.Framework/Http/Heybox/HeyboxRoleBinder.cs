using System;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Unified;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    /// <summary>
    /// 「绑定角色」流程：发起绑定后，若服务端表示仍在处理中（waiting），按固定节奏轮询绑定状态，
    /// 超过上限判失败。成功后该角色归属当前登录账号，战绩数据即可查到。
    /// <para>
    /// 后端业务失败会以 <see cref="NarakaApiException"/> 带 msg 上抛，由调用方展示原文；
    /// 轮询超限 / 请求超时等「没有后端文案」的失败返回 <see cref="RoleBindOutcome.Failed"/>
    /// 且 ErrorMessage 为空，由 UI 层补默认文案。
    /// </para>
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxRoleBinder
    {
        private const string GameType = "yjwj";

        /// <summary>
        /// 绑定请求需要显式携带的客户端形态标识（官方前端口径：仅绑定这一处覆盖默认值，其余请求不带）。
        /// </summary>
        private const string OsType = "webinapp";

        private const string StateOk = "ok";

        private const string StateWaiting = "waiting";

        /// <summary>轮询次数上限（对齐网页端 &gt; 5 即判失败的判定）。</summary>
        internal const int MaxStatePollingAttempts = 5;

        /// <summary>轮询间隔（对齐网页端 2 秒的节奏）。</summary>
        internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// 单请求超时。与 <see cref="HeyboxPlayerRefresher"/> 同基准：请求走进程级串行闸门，
        /// 一条卡住的请求会把后面全部堵住，8 秒足以覆盖正常往返。
        /// </summary>
        private const int RequestTimeoutMilliseconds = 8000;

        public Task<RoleBindOutcome> BindAsync(string gameId, int serverId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                return Task.FromResult(RoleBindOutcome.Failed(null));

            return BindCoreAsync(gameId.Trim(), serverId, RequestBindAsync, RequestStateAsync, ct);
        }

        /// <summary>
        /// 查询当前登录账号绑定的角色（绑定弹窗「已绑定」视图用）。
        /// <para>
        /// 机制：**不带 role_id 查主页数据**——服务端按登录账号找它绑定的角色，未绑定时回
        /// bind_account=0 或空壳（网页端绑定成功后重拉的就是这条请求，所以刚绑定的角色立刻可见）。
        /// </para>
        /// <para>返回 null = 未绑定 / 查不到 / 请求失败——调用方静默退回绑定输入界面，不弹错。</para>
        /// </summary>
        public async Task<BoundRoleInfo?> GetBoundRoleAsync(CancellationToken ct)
        {
            try
            {
                return await SendWithTimeoutAsync<HeyboxHomeResponse, BoundRoleInfo>(
                    token => NarakaApiClient.GetPlayerHomeAsync(
                        server: null,
                        roleId: null,
                        season: null,
                        battleTid: null,
                        ct: token),
                    response => ToBoundRoleInfo(response?.Result),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(HeyboxRoleBinder)}.{nameof(GetBoundRoleAsync)}", "query bound role failed");
                return null;
            }
        }

        /// <summary>从「不带 role_id 的主页数据」响应里读出账号绑定的角色；未绑定 / 空壳返回 null。</summary>
        internal static BoundRoleInfo? ToBoundRoleInfo(HeyboxHomeData? data)
        {
            if (data is null) return null;

            // bind_account == 0 是服务端明确下发的「该角色没绑到任何账号」——本查询里即"当前账号未绑定"。
            if (data.BindAccount == 0) return null;
            if (string.IsNullOrEmpty(data.RoleId)) return null;
            if (data.PlayerInfo is null || string.IsNullOrEmpty(data.PlayerInfo.Name)) return null;

            return new BoundRoleInfo(
                data.RoleId,
                data.Server ?? string.Empty,
                data.ServerDesc ?? string.Empty,
                data.PlayerInfo.Name!,
                data.PlayerInfo.Avatar ?? string.Empty,
                UnifiedValueParser.ParseLooseNumber(data.PlayerInfo.Lv));
        }

        /// <summary>
        /// 当前账号是否绑定过角色（与 <see cref="GetBoundRoleAsync"/> 同一条查询，但保留三态）：
        /// <c>true</c> = 已绑定；<c>false</c> = 服务端明确未绑定；<c>null</c> = 请求没成功（无从判断）。
        /// <para>
        /// 给「引导入口只在未绑定时出现」这类显隐判断用——拿不准时必须与"明确未绑定"区分开，
        /// 否则网络抖动时会把已绑定账号也判成未绑定。
        /// </para>
        /// </summary>
        public async Task<bool?> HasBoundRoleAsync(CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(RequestTimeoutMilliseconds);

            try
            {
                var response = await NarakaApiClient.GetPlayerHomeAsync(
                    server: null,
                    roleId: null,
                    season: null,
                    battleTid: null,
                    ct: linked.Token).ConfigureAwait(false);

                // 响应拿到了（信封 status ok）才敢下结论："有角色信息 / bind_account 非 0" = 已绑定。
                return ToBoundRoleInfo(response?.Result) is not null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(HeyboxRoleBinder)}.{nameof(HasBoundRoleAsync)}", "query bound role state failed");
                return null;
            }
        }

        /// <summary>
        /// 状态机本体，请求通过委托注入以便离线回归（同 <see cref="HeyboxPlayerRefresher"/> 的模式）。
        /// </summary>
        internal async Task<RoleBindOutcome> BindCoreAsync(
            string gameId,
            int serverId,
            Func<string, int, CancellationToken, Task<string?>> requestBind,
            Func<string, CancellationToken, Task<string?>> requestState,
            CancellationToken ct,
            TimeSpan? pollInterval = null)
        {
            var interval = pollInterval ?? PollInterval;

            // 发起绑定。state 为 null 表示请求超时 / 网络错误（无文案可展示）；
            // 后端业务失败会直接抛 NarakaApiException，不在这里吞。
            var state = await requestBind(gameId, serverId, ct).ConfigureAwait(false);
            if (IsOk(state)) return RoleBindOutcome.Succeeded();
            if (!IsWaiting(state)) return RoleBindOutcome.Failed(null);

            // waiting：服务端异步处理中，按网页端的固定节奏轮询，最多 5 次。
            for (var attempt = 0; attempt < MaxStatePollingAttempts; attempt++)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                state = await requestState(gameId, ct).ConfigureAwait(false);
                if (IsOk(state)) return RoleBindOutcome.Succeeded();
                if (!IsWaiting(state)) return RoleBindOutcome.Failed(null);
            }

            // 5 次轮询都还是 waiting：对齐网页端的默认失败文案分支。
            return RoleBindOutcome.Failed(null);
        }

        private static bool IsOk(string? state)
            => string.Equals(state, StateOk, StringComparison.OrdinalIgnoreCase);

        private static bool IsWaiting(string? state)
            => string.Equals(state, StateWaiting, StringComparison.OrdinalIgnoreCase);

        private static Task<string?> RequestBindAsync(string gameId, int serverId, CancellationToken ct)
            => SendWithTimeoutAsync<HeyboxBindGameIdResponse, string>(
                token => NarakaApiClient.BindGameIdAsync(
                    gameType: GameType,
                    gameId: gameId,
                    osType: OsType,
                    serverId: serverId,
                    ct: token),
                response => response?.Result?.State,
                ct);

        private static Task<string?> RequestStateAsync(string gameId, CancellationToken ct)
            => SendWithTimeoutAsync<HeyboxBindGameStateResponse, string>(
                token => NarakaApiClient.BindGameStateAsync(
                    gameType: GameType,
                    gameId: gameId,
                    osType: OsType,
                    ct: token),
                response => response?.Result?.State,
                ct);

        /// <summary>
        /// 单请求的公共收尾：加总超时；超时与网络错误返回 null（按「没有后端文案的失败」处理）。
        /// 用户取消（弹窗已关等）与后端业务失败（msg 要原样展示）继续上抛。
        /// </summary>
        private static async Task<TResult?> SendWithTimeoutAsync<TResponse, TResult>(
            Func<CancellationToken, Task<TResponse>> send,
            Func<TResponse, TResult?> extract,
            CancellationToken ct)
            where TResult : class
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(RequestTimeoutMilliseconds);

            try
            {
                var response = await send(linked.Token).ConfigureAwait(false);
                return extract(response);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NarakaApiException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
