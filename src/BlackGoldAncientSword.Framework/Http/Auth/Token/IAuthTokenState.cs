using System;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 进程内 token 快照。<see cref="AuthTokenHandler"/> 读它决定是否加 Bearer；
    /// 登录页 / 401 处理 / logout 通过 <see cref="Set"/> 更新。
    /// </summary>
    public interface IAuthTokenState
    {
        AuthToken? Current { get; }

        event EventHandler<AuthToken?> Changed;

        void Set(AuthToken? token);
    }
}
