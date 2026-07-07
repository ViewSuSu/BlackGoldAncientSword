using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Auth.MemberProfile
{
    /// <summary>
    /// 与网页版 <c>auth.fetchUserInfo()</c> 行为对齐：登录成功后单独调
    /// <c>GET /app-api/member/user/get</c> 拉 <c>nickname / avatar / mobile / ...</c>。
    /// 微信 QR 登录响应本身不含 profile，必须靠这个二次调用补齐 UI 展示字段。
    /// </summary>
    public interface IMemberProfileService
    {
        /// <summary>
        /// 用给定 accessToken 拉当前会员 profile；返回小 JSON（含 userId/nickname/avatar），失败返 null。
        /// 传参而非从 <c>IAuthTokenState.Current</c> 拿，用于 <c>tokenState.Set</c> 之前也能调用（避免时序耦合）。
        /// </summary>
        Task<string?> GetProfileJsonAsync(string accessToken, CancellationToken ct = default);
    }
}
