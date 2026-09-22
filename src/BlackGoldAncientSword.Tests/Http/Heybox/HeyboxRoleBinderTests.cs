using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    /// <summary>
    /// 绑定角色状态机（对齐网页端的处理中轮询与失败分支）的离线回归：
    /// 不联网、请求用委托注入，只验状态流转与失败分支。
    /// </summary>
    public class HeyboxRoleBinderTests
    {
        private static Func<string, CancellationToken, Task<string?>> States(params string?[] states)
        {
            var queue = new Queue<string?>(states);
            return (_, _) => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : null);
        }

        [Fact]
        public async Task BindAsync_ReturnsSucceededImmediatelyWhenStateOk()
        {
            var stateCalls = 0;
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => Task.FromResult<string?>("ok"),
                (_, _) => { stateCalls++; return Task.FromResult<string?>("ok"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.True(outcome.Success);
            Assert.Equal(0, stateCalls);
        }

        [Fact]
        public async Task BindAsync_PollsWhileWaitingThenSucceeds()
        {
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => Task.FromResult<string?>("waiting"),
                States("waiting", "ok"),
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.True(outcome.Success);
            Assert.Null(outcome.ErrorMessage);
        }

        [Fact]
        public async Task BindAsync_StopsAfterPollingCeiling()
        {
            var stateCalls = 0;
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => Task.FromResult<string?>("waiting"),
                (_, _) => { stateCalls++; return Task.FromResult<string?>("waiting"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(outcome.Success);
            // 轮询超限属于"没有后端文案的失败"，由 UI 层补默认文案。
            Assert.Null(outcome.ErrorMessage);
            Assert.Equal(HeyboxRoleBinder.MaxStatePollingAttempts, stateCalls);
        }

        [Fact]
        public async Task BindAsync_StopsOnTerminalStateWithoutPolling()
        {
            var stateCalls = 0;
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => Task.FromResult<string?>("waiting"),
                (_, _) => { stateCalls++; return Task.FromResult<string?>("failed"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(outcome.Success);
            Assert.Equal(1, stateCalls);
        }

        [Fact]
        public async Task BindAsync_FailsWithoutPollingWhenInitialStateTerminal()
        {
            var stateCalls = 0;
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => Task.FromResult<string?>("failed"),
                (_, _) => { stateCalls++; return Task.FromResult<string?>("ok"); },
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(outcome.Success);
            Assert.Equal(0, stateCalls);
        }

        [Fact]
        public async Task BindAsync_PropagatesBackendErrorMessage()
        {
            // 信封失败（status 非 ok）带 msg：原样上抛，UI 层才能展示后端文案。
            var binder = new HeyboxRoleBinder();

            var ex = await Assert.ThrowsAsync<NarakaApiException>(() => binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => throw new NarakaApiException(0, "该角色已被其他账号绑定"),
                (_, _) => Task.FromResult<string?>("ok"),
                CancellationToken.None,
                TimeSpan.Zero));

            Assert.Equal("该角色已被其他账号绑定", ex.Msg);
        }

        [Fact]
        public async Task BindAsync_ReturnsFailureWithoutMessageWhenRequestTimedOut()
        {
            // 超时 / 网络错误：state 为 null，按"无文案的失败"处理，不抛。
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindCoreAsync(
                "玩家甲",
                163,
                (_, _, _) => Task.FromResult<string?>(null),
                (_, _) => Task.FromResult<string?>("ok"),
                CancellationToken.None,
                TimeSpan.Zero);

            Assert.False(outcome.Success);
            Assert.Null(outcome.ErrorMessage);
        }

        [Fact]
        public async Task BindAsync_ThrowsWhenCancelled()
        {
            var binder = new HeyboxRoleBinder();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => binder.BindCoreAsync(
                "玩家甲",
                163,
                async (_, _, ct) => { await Task.Delay(1, ct); return "waiting"; },
                (_, _) => Task.FromResult<string?>("ok"),
                cts.Token,
                TimeSpan.Zero));
        }

        [Fact]
        public async Task BindAsync_RejectsBlankGameId()
        {
            var binder = new HeyboxRoleBinder();

            var outcome = await binder.BindAsync("   ", 163, CancellationToken.None);

            Assert.False(outcome.Success);
            Assert.Null(outcome.ErrorMessage);
        }

        // === ToBoundRoleInfo：账号已绑定角色的响应映射（不带 role_id 的主页数据）===

        [Fact]
        public void ToBoundRoleInfo_ReturnsNullForNullResult()
        {
            Assert.Null(HeyboxRoleBinder.ToBoundRoleInfo(null));
        }

        [Fact]
        public void ToBoundRoleInfo_ReturnsNullWhenAccountHasNoBoundRole()
        {
            // bind_account == 0：服务端明确表示该角色没绑到任何账号（本查询里即"当前账号未绑定"）。
            var data = new HeyboxHomeData
            {
                BindAccount = 0,
                RoleId = "l77c000015949400120163",
                PlayerInfo = new HeyboxPlayerInfo { Name = "某个角色" },
            };

            Assert.Null(HeyboxRoleBinder.ToBoundRoleInfo(data));
        }

        [Fact]
        public void ToBoundRoleInfo_ReturnsNullForEmptyShell()
        {
            var data = new HeyboxHomeData { BindAccount = 1, RoleId = string.Empty, PlayerInfo = null };

            Assert.Null(HeyboxRoleBinder.ToBoundRoleInfo(data));
        }

        [Fact]
        public void ToBoundRoleInfo_MapsBoundRole()
        {
            var data = new HeyboxHomeData
            {
                BindAccount = 1,
                RoleId = "l77c000015949400120163",
                Server = "163",
                ServerDesc = "国服",
                PlayerInfo = new HeyboxPlayerInfo
                {
                    Name = "爱的供养丶",
                    Avatar = "https://example.invalid/a.png",
                    Lv = "439",
                },
            };

            var role = HeyboxRoleBinder.ToBoundRoleInfo(data);

            Assert.NotNull(role);
            Assert.Equal("l77c000015949400120163", role!.RoleId);
            Assert.Equal("163", role.Server);
            Assert.Equal("国服", role.ServerDesc);
            Assert.Equal("爱的供养丶", role.Name);
            Assert.Equal("https://example.invalid/a.png", role.Avatar);
            Assert.Equal(439, role.Level);
        }
    }
}
