using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public interface IHeyboxSessionProbe
    {

        Task<HeyboxSessionProbeOutcome> ProbeAsync(
            string roleId,
            string server,
            CancellationToken ct = default);
    }
}
