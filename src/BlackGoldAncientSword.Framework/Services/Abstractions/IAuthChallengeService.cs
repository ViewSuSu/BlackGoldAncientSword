using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{

    public interface IAuthChallengeService
    {
        Task<bool> ShowAsync(CancellationToken ct = default);

        void Complete(bool success);
    }
}
