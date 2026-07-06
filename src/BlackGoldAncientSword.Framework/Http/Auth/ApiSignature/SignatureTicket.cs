namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    public sealed record SignatureTicket(string AppId, string AppSecret, long ExpireTime);
}
