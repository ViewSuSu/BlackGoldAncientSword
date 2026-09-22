using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    public class HeyboxSessionRestorerTests
    {
        private const string RoleId = "l77c000015949400120163";

        private static readonly HeyboxSession Session = new("99365688", "secret-pkey");

        private static readonly string Server = PlayerSourceContext.ServerFromRoleId(RoleId);

        [Fact]
        public async Task TryRestoreAsync_Publishes_Session_Before_Probe_Runs()
        {
            var state = new HeyboxSessionState();
            HeyboxLoginState? stateAtProbe = null;

            var probe = new FakeProbe((_, _) =>
            {
                stateAtProbe = state.Current;
                return new HeyboxSessionProbeOutcome(true, null);
            });

            var restorer = new HeyboxSessionRestorer(state, NewProvider(), probe);

            var restored = await restorer.TryRestoreAsync(Session, RoleId, Server);

            Assert.True(restored);
            Assert.NotNull(stateAtProbe);
            Assert.Equal(Session, stateAtProbe!.Session);
            Assert.Equal(RoleId, probe.LastRoleId);
            Assert.Equal(Server, probe.LastServer);
        }

        [Fact]
        public async Task TryRestoreAsync_Seeds_Probed_Home_Data_For_Reuse()
        {
            var home = new HeyboxHomeResponse { Status = "ok" };
            var provider = NewProvider();

            var restorer = new HeyboxSessionRestorer(
                new HeyboxSessionState(),
                provider,
                new FakeProbe((_, _) => new HeyboxSessionProbeOutcome(true, home)));

            Assert.True(await restorer.TryRestoreAsync(Session, RoleId, Server));

            var reused = await provider.GetAsync(
                RoleId, Server, season: null, battleTid: null, CancellationToken.None);

            Assert.Same(home, reused);
        }

        [Fact]
        public async Task TryRestoreAsync_Unpublishes_Session_When_Probe_Rejects()
        {
            var state = new HeyboxSessionState();

            var restorer = new HeyboxSessionRestorer(
                state,
                NewProvider(),
                new FakeProbe((_, _) => new HeyboxSessionProbeOutcome(false, null)));

            var restored = await restorer.TryRestoreAsync(Session, RoleId, Server);

            Assert.False(restored);
            Assert.Null(state.Current);
        }

        [Fact]
        public async Task TryRestoreAsync_Keeps_Session_When_Probe_Throws()
        {
            var state = new HeyboxSessionState();

            var restorer = new HeyboxSessionRestorer(state, NewProvider(), new ThrowingProbe());

            var restored = await restorer.TryRestoreAsync(Session, RoleId, Server);

            Assert.True(restored);
            Assert.NotNull(state.Current);
        }

        [Fact]
        public async Task TryRestoreAsync_Skips_Probe_When_Session_Is_Null()
        {
            var state = new HeyboxSessionState();
            var probe = new FakeProbe((_, _) => new HeyboxSessionProbeOutcome(true, null));

            var restorer = new HeyboxSessionRestorer(state, NewProvider(), probe);

            var restored = await restorer.TryRestoreAsync(null, RoleId, Server);

            Assert.False(restored);
            Assert.Equal(0, probe.Calls);
            Assert.Null(state.Current);
        }

        [Fact]
        public async Task TryRestoreAsync_Does_Not_Delete_Stored_File_When_Probe_Rejects()
        {
            var path = NewTempPath();
            var store = new DpapiHeyboxSessionStore(path);

            try
            {
                store.Save(Session);

                var restorer = new HeyboxSessionRestorer(
                    new HeyboxSessionState(),
                    NewProvider(),
                    new FakeProbe((_, _) => new HeyboxSessionProbeOutcome(false, null)));

                var restored = await restorer.TryRestoreAsync(store.Load(), RoleId, Server);

                Assert.False(restored);
                Assert.True(File.Exists(path));
                Assert.Equal(Session.Pkey, store.Load()!.Pkey);
            }
            finally
            {
                store.Clear();
            }
        }

        private static HeyboxHomeDataProvider NewProvider() => new(new HeyboxRequestCache());

        private static string NewTempPath() =>
            Path.Combine(Path.GetTempPath(), "bgas-session-" + Guid.NewGuid().ToString("N") + ".dat");

        private sealed class FakeProbe : IHeyboxSessionProbe
        {
            private readonly Func<string, string, HeyboxSessionProbeOutcome> _respond;

            public FakeProbe(Func<string, string, HeyboxSessionProbeOutcome> respond) => _respond = respond;

            public int Calls { get; private set; }

            public string? LastRoleId { get; private set; }

            public string? LastServer { get; private set; }

            public Task<HeyboxSessionProbeOutcome> ProbeAsync(
                string roleId, string server, CancellationToken ct = default)
            {
                Calls++;
                LastRoleId = roleId;
                LastServer = server;
                return Task.FromResult(_respond(roleId, server));
            }
        }

        private sealed class ThrowingProbe : IHeyboxSessionProbe
        {
            public Task<HeyboxSessionProbeOutcome> ProbeAsync(
                string roleId, string server, CancellationToken ct = default)
                => throw new HttpRequestException("boom");
        }
    }
}
