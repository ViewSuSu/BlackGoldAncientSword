using System;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public sealed record HeyboxLoginState(HeyboxSession Session, string Nickname, string Avatar)
    {
        public static HeyboxLoginState FromSession(HeyboxSession session) => new(session, string.Empty, string.Empty);
    }

    public interface IHeyboxSessionState
    {
        HeyboxLoginState? Current { get; }

        event EventHandler<HeyboxLoginState?>? Changed;

        void Set(HeyboxLoginState? state);
    }
}
