using System;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    [Component(ComponentLifetime.Singleton)]
    public sealed class AuthTokenState : IAuthTokenState
    {
        private readonly object _sync = new();
        private AuthToken? _current;

        public AuthToken? Current
        {
            get { lock (_sync) return _current; }
        }

        public event EventHandler<AuthToken?>? Changed;

        public void Set(AuthToken? token)
        {
            AuthToken? previous;
            lock (_sync)
            {
                previous = _current;
                _current = token;
            }

            if (!ReferenceEquals(previous, token))
                Changed?.Invoke(this, token);
        }
    }
}
