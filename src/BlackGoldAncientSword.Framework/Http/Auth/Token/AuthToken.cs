namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 与 <c>localStorage.wushenbang_token / wushenbang_refresh_token / wushenbang_user</c> 三个键一一对应的本地凭证。
    /// <see cref="ExpiresAtUnixMs"/> 由 <see cref="JwtExpiryReader"/> 从 <see cref="AccessToken"/> 的 JWT payload
    /// <c>exp</c>（秒）解出后 * 1000。若 token 非 JWT 或解析失败，取 0（视为立即过期，会强制走 refresh 或重登）。
    /// </summary>
    public sealed record AuthToken(
        string AccessToken,
        string RefreshToken,
        string? UserJson,
        long ExpiresAtUnixMs);
}
