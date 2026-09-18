using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public interface IHeyboxBrowserLoginService
    {

        Task<HeyboxSession?> LoginAsync(CancellationToken ct = default);
    }
}
