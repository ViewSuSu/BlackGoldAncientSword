namespace BlackGoldAncientSword.GameMonitor.Services.Abstractions
{
    /// <summary>
    /// 玩家偏好数据服务接口。从永劫无间 player_prefs.txt 异步读取玩家信息。
    /// </summary>
    public interface IPlayerPrefsService
    {
        PlayerPrefsData Current { get; }

        /// <summary>异步加载玩家偏好数据。</summary>
        Task LoadAsync();
    }
}
