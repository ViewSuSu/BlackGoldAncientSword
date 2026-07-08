namespace BlackGoldAncientSword.Framework.Core.Events
{
    /// <summary>
    /// 用户在"发现新版本"卡片点击"在线更新"后，UpdaterExe 已成功拉起，主 App 应立即进入"全屏锁死等重启"状态。
    /// <see cref="Shell.MainWindowViewModel"/> 订阅本事件把 IsOnlineUpdating 置为 true，触发顶层遮罩显示；
    /// updateGate 故意不 Complete，让 App.OnStartup [4] 保持挂起，避免继续走登录 gate 与主页导航。
    /// Updater 侧下载 + 解压 + 覆盖完毕后会用 --main-pid 关掉主进程并重启新版，本事件不需要"结束"路径。
    /// </summary>
    public class OnlineUpdatingStartedEvent : PubSubEvent { }
}
