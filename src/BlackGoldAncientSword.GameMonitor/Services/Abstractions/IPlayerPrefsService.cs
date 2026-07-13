namespace BlackGoldAncientSword.GameMonitor.Services.Abstractions
{
    /// <summary>
    /// 玩家偏好数据服务接口。从永劫无间 player_prefs.txt 异步读取玩家信息。
    /// 调用方在需要"最新的本地登录账号"的时机（如进入战绩页）应主动 <c>await LoadAsync()</c>；
    /// <see cref="Current"/> 只是最近一次成功加载的快照。
    /// </summary>
    public interface IPlayerPrefsService
    {
        PlayerPrefsData Current { get; }

        /// <summary>异步加载玩家偏好数据。</summary>
        Task LoadAsync();
    }
}
