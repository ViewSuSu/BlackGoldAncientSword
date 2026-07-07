namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 与 <c>localStorage.wushenbang_token / wushenbang_refresh_token / wushenbang_user</c> 三个键一一对应的本地凭证。
    /// <see cref="ExpiresAtUnixMs"/> 优先取服务器 <c>expiresTime</c>（yudao AuthLoginRespVO，Long Unix ms）；
    /// 服务器未提供时回退 <see cref="JwtExpiryReader"/>（仅对 JWT token 有效）。opaque token 无法解析 JWT，
    /// 必须依赖服务器字段——否则 0 会被本地过期检查判为立即过期，导致每次启动都强制重登。
    /// </summary>
    public sealed record AuthToken(
        string AccessToken,
        string RefreshToken,
        string? UserJson,
        long ExpiresAtUnixMs);
}
