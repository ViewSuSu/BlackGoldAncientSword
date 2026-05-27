namespace BlackGoldAncientSword.GameMonitor.Services.Abstractions
{
    /// <summary>
    /// 游戏状态监控器接口。通过截屏+OCR检测游戏当前状态（大厅等候/排队中等）。
    /// </summary>
    public interface IGameStatusMonitor : IDisposable
    {
        /// <summary>每次OCR识别完成后触发，订阅方从事件参数中获取本次识别结果。</summary>
        event EventHandler<GameStatusChangedEventArgs>? GameStatusRecognized;

        /// <summary>是否正在运行监控。</summary>
        bool IsRunning { get; }

        /// <summary>启动后台监控。</summary>
        void Start();

        /// <summary>停止后台监控。</summary>
        void Stop();
    }
}