using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class HeyboxLiveCollection
    {
        public const string Name = "HeyboxLive";
    }

    internal static class HeyboxLiveSetup
    {
        public const int IntervalMilliseconds = 5000;

        public static HeyboxSession? LoadSession() => new DpapiHeyboxSessionStore().Load();

        public static void ConfigureClient(HeyboxSession session)
        {
            var state = new HeyboxSessionState();
            state.Set(HeyboxLoginState.FromSession(session));
            NarakaApiClient.Configure(new HeyboxRequestPacingHandler
            {
                IntervalMilliseconds = IntervalMilliseconds,
                InnerHandler = new HeyboxSignatureHandler(state)
                {
                    InnerHandler = new HttpClientHandler { UseCookies = false },
                },
            });
        }
    }
}
