using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    /// <summary>
    /// 游戏状态监视器。状态完全由 IGameLogMonitor 的日志事件驱动，
    /// 不再执行截屏或 OCR，仅作为状态事件的中转。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class GameStatusMonitor : IGameStatusMonitor
    {
        public event EventHandler<GameStatusChangedEventArgs>? GameStatusRecognized;

        public bool IsRunning { get; private set; }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            GameStatusRecognized?.Invoke(this, new GameStatusChangedEventArgs(GameStatus.Unknown));
        }

        public void NotifyStatus(GameStatus status)
        {
            GameStatusRecognized?.Invoke(this, new GameStatusChangedEventArgs(status));
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
