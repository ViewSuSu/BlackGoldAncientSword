using System.Diagnostics;
using System.Runtime.InteropServices;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Ocr;
using BlackGoldAncientSword.ScreenCapture;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class GameStatusMonitor : IGameStatusMonitor
    {
        private const string GameProcessName = "NarakaBladepoint";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        private readonly IScreenCaptureService _screenCapture;
        private readonly IOcrService _ocrService;

        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private readonly object _lock = new();
        private bool _disposed;

        public event EventHandler<GameStatusChangedEventArgs>? GameStatusRecognized;

        public bool IsRunning { get; private set; }

        public GameStatusMonitor(IScreenCaptureService screenCapture, IOcrService ocrService)
        {
            _screenCapture = screenCapture;
            _ocrService = ocrService;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _monitorTask = Task.Run(() => MonitorLoop(token), token);
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            GameStatusRecognized?.Invoke(this, new GameStatusChangedEventArgs(GameStatus.Unknown));
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var status = await DetectGameStatusAsync();
                    GameStatusRecognized?.Invoke(this, new GameStatusChangedEventArgs(status));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(PollInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<GameStatus> DetectGameStatusAsync()
        {
            if (!TryGetGameWindow(out var hwnd))
                return GameStatus.Unknown;

            if (IsIconic(hwnd))
                return GameStatus.Unknown;

            try
            {
                var bytes = _screenCapture.CaptureRegion(hwnd, ScreenQuadrant.BottomRight);
                var text = await _ocrService.RecognizeTextAsync(bytes);

                if (text.Contains("取消") || text.Contains("取") || text.Contains("消"))
                    return GameStatus.Queuing;

                if (text.Contains("开始游戏") || text.Contains("开始") || text.Contains("游戏"))
                    return GameStatus.LobbyWaiting;
            }
            catch
            {
            }

            return GameStatus.Unknown;
        }

        private static bool TryGetGameWindow(out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            try
            {
                var processes = Process.GetProcessesByName(GameProcessName);
                foreach (var p in processes)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        hwnd = p.MainWindowHandle;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }
}