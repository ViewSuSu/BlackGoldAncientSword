using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 用 refresh token 换新 access token。抽出接口是因为具体 endpoint 也需要签名，
    /// 而它得注入 <see cref="ApiSignature.SignatureHandler"/> 的同一条 handler 链——由 App 层组装时
    /// 传入一个循环引用打破的实现。
    /// </summary>
    public interface IAuthTokenRefresher
    {
        Task<AuthToken?> RefreshAsync(string refreshToken, CancellationToken ct);
    }
}
