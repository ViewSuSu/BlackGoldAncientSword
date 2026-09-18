using System;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{
    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxSessionState : IHeyboxSessionState
    {
        private readonly object _sync = new();
        private HeyboxLoginState? _current;

        public HeyboxLoginState? Current
        {
            get { lock (_sync) return _current; }
        }

        public event EventHandler<HeyboxLoginState?>? Changed;

        public void Set(HeyboxLoginState? state)
        {
            HeyboxLoginState? previous;
            lock (_sync)
            {
                previous = _current;
                _current = state;
            }

            if (!ReferenceEquals(previous, state))
                Changed?.Invoke(this, state);
        }
    }
}
