using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public interface IHeyboxSessionRestorer
    {

        Task<bool> TryRestoreAsync(
            HeyboxSession? session,
            string roleId,
            string server,
            CancellationToken ct = default);
    }
}
