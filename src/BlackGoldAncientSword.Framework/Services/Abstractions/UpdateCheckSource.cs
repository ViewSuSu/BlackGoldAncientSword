namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 版本检查的发起来源，用于区分该次检查触发后应如何呈现 UI。
    /// </summary>
    public enum UpdateCheckSource
    {
        /// <summary>App 启动时的首次检测：命中新版走启动 gate + 弹更新卡片。</summary>
        Startup,

        /// <summary>用户点击左下角"发现新版本"主动重查：命中新版弹更新卡片。</summary>
        UserManual,

        /// <summary>后台定时轮询：命中新版只点亮左下角"发现新版本"指示，不弹卡片。</summary>
        Background,
    }
}
