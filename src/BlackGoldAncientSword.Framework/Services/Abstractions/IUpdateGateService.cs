using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 启动期"新版本通知"门槛：App 层 await 用户操作（在线更新 / 打开浏览器 / 稍后再说 / 关闭）
    /// 结束后才继续走登录 gate 与后续导航。
    /// 与 <see cref="IAuthChallengeService"/> 同样的 TaskCompletionSource 单飞模式。
    /// </summary>
    public interface IUpdateGateService
    {
        /// <summary>
        /// 等待用户处理更新弹窗；无论用户选哪种操作，DismissOverlay 都会触发 <see cref="Complete"/>。
        /// 未弹出时调用会一直挂起——调用方需在确认 IsUpdateAvailable 为 true 时才 await。
        /// </summary>
        Task WaitAsync(CancellationToken ct = default);

        /// <summary>由 <c>UpdateNotificationPageViewModel</c> 在关闭弹窗时调用。</summary>
        void Complete();
    }
}
