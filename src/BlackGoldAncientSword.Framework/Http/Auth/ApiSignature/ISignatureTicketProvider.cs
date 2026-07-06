namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    public interface ISignatureTicketProvider
    {
        System.Threading.Tasks.Task<SignatureTicket> GetAsync(System.Threading.CancellationToken ct = default);

        void Invalidate();
    }
}
