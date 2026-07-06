namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 持久化 token 到本地磁盘（跨启动保留）。清空即注销。
    /// </summary>
    public interface IAuthTokenStore
    {
        AuthToken? Load();

        void Save(AuthToken token);

        void Clear();
    }
}
