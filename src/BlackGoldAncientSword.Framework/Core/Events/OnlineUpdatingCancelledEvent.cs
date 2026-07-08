namespace BlackGoldAncientSword.Framework.Core.Events
{
    /// <summary>
    /// Updater 独立进程在主 App 被 kill 前先自行退出——说明用户在 Updater 侧点了取消 / 下载失败等，
    /// 需要把主 App 从"在线更新锁死"状态恢复回正常：关掉 OnlineUpdating overlay + 释放 updateGate 让
    /// App.OnStartup 继续走 [5] 登录 gate 与 [6] 主页导航。
    /// </summary>
    public class OnlineUpdatingCancelledEvent : PubSubEvent { }
}
