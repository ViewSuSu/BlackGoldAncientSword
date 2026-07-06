namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    /// <summary>
    /// 网页 <c>naraka-h5</c> 客户端约定的签名协议常量。
    /// 与 <c>https://naraka.drivod.top/assets/index-*.js</c> 中 <c>D7/O7/k7</c> 对齐。
    /// </summary>
    public static class SignatureConstants
    {
        public const string AppId = "naraka-h5";

        public const string TicketPath = "/app-api/system/api-signature/ticket";

        public const string HeaderAppId = "appId";

        public const string HeaderTimestamp = "timestamp";

        public const string HeaderNonce = "nonce";

        public const string HeaderSign = "sign";

        public const long TicketRefreshLeadTimeMs = 10_000;
    }
}
