namespace BlackGoldAncientSword.GameMonitor.Models
{
    /// <summary>
    /// 当前游戏状态枚举。
    /// </summary>
    public enum GameStatus
    {
        /// <summary>未知/未检测到游戏</summary>
        Unknown,
        /// <summary>大厅等候</summary>
        LobbyWaiting,
        /// <summary>排队中</summary>
        Queuing,
    }
}