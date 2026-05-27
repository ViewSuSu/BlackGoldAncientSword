namespace BlackGoldAncientSword.GameMonitor.Services.Abstractions
{
    /// <summary>
    /// 游戏日志监视器接口。异步监听永劫无间日志文件，
    /// 检测进入/离开对局事件。
    /// </summary>
    public interface IGameLogMonitor : IDisposable
    {
        event EventHandler<BattleEventArgs>? BattleStarted;
        event EventHandler<BattleEventArgs>? BattleEnded;
        event EventHandler<BattleEventArgs>? BattleJoined;

        string? CurrentBattleId { get; }
        bool IsInBattle { get; }
        bool IsRunning { get; }

        /// <summary>异步启动日志监视。</summary>
        Task StartAsync();
        void Stop();
    }
}
