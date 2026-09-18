using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public sealed record HeyboxQrChallenge(string Uuid, byte[] ImageBytes);

    public enum HeyboxQrOutcome
    {
        WaitingScan,
        Scanned,
        Success,
        Expired,
        Failed,
    }

    public sealed record HeyboxQrPollResult(HeyboxQrOutcome Outcome, HeyboxSession? Session);

    public interface IHeyboxQrLoginService
    {
        Task<HeyboxQrChallenge?> CreateAsync(CancellationToken ct);

        Task<HeyboxQrPollResult> PollAsync(string uuid, CancellationToken ct);
    }
}
