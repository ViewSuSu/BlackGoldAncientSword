using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 弹出登录 Overlay 并等待用户扫码完成。多个并发调用只弹一次；用户完成后所有 await 者一同 resume。
    /// 具体实现在 App 层，因为要 <see cref="Prism.Regions.IRegionManager"/> + UI Dispatcher。
    /// </summary>
    public interface IAuthChallengeService
    {
        Task<bool> ShowAsync(CancellationToken ct = default);

        void Complete(bool success);
    }
}
