namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    /// <summary>
    /// 桌面 <c>naraka-desktop</c> 客户端约定的签名协议常量。
    /// 与 <c>https://naraka.drivod.top/assets/index-*.js</c> 中 <c>P7/j7/M7</c> 对齐（真实算法参见
    /// <see cref="RequestSigner"/> 注释；旧版 <c>D7/O7/k7</c> 已废弃）。
    /// </summary>
    public static class SignatureConstants
    {
        public const string AppId = "naraka-desktop";

        public const string TicketPath = "/app-api/system/api-signature/ticket";

        public const string HeaderAppId = "appId";

        public const string HeaderTimestamp = "timestamp";

        public const string HeaderNonce = "nonce";

        public const string HeaderSign = "sign";

        public const long TicketRefreshLeadTimeMs = 10_000;
    }
}
