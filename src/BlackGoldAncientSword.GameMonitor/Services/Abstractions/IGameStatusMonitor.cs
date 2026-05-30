namespace BlackGoldAncientSword.GameMonitor.Services.Abstractions
{
    /// <summary>
    /// 游戏状态监控器接口。状态由 IGameLogMonitor 通过日志事件驱动，
    /// 不再使用截屏+OCR。
    /// </summary>
    public interface IGameStatusMonitor : IDisposable
    {
        /// <summary>获取当前游戏状态。</summary>
        GameStatus CurrentStatus { get; }

        /// <summary>状态变更时触发，订阅方从事件参数中获取当前状态。</summary>
        event EventHandler<GameStatusChangedEventArgs>? GameStatusRecognized;

        /// <summary>是否正在运行。</summary>
        bool IsRunning { get; }

        /// <summary>启动监控。</summary>
        void Start();

        /// <summary>停止监控。</summary>
        void Stop();

        /// <summary>由 IGameLogMonitor 推送游戏状态，直接触发 GameStatusRecognized 事件。</summary>
        void NotifyStatus(GameStatus status);
    }
}
