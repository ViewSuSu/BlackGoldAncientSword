using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.Home.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private const int PollIntervalMs = 2000;
        private readonly IGameLogMonitor _gameLogMonitor;
        private readonly IGameStatusMonitor _gameStatusMonitor;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly ILocalizedTextProvider _localizedText;
        private CancellationTokenSource? _processCheckCts;

        public HomePageViewModel(
            IGameLogMonitor gameLogMonitor,
            IGameStatusMonitor gameStatusMonitor,
            IUIDispatcher uiDispatcher,
            ILocalizedTextProvider localizedText)
        {
            _gameLogMonitor = gameLogMonitor;
            _gameStatusMonitor = gameStatusMonitor;
            _uiDispatcher = uiDispatcher;
            _localizedText = localizedText;

            StatusText = _localizedText.Get("Home.Status.WaitingForGame", "等待游戏启动");
            IsLoading = true;
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        private string _statusHint = string.Empty;
        public string StatusHint
        {
            get => _statusHint;
            set
            {
                if (_statusHint == value) return;
                _statusHint = value;
                RaisePropertyChanged(nameof(StatusHint));
            }
        }

        private bool _isGameRunning;
        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                if (_isGameRunning == value) return;
                _isGameRunning = value;
                RaisePropertyChanged(nameof(IsGameRunning));
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
            }
        }

        private bool _isSubscribed;
        // 不再是事件 handler，由 RunProcessCheckLoopAsync 显式 await 调用；
        // 返回 Task 让异常能在 caller 处被捕获，避免 async void 心智负担。
        private async Task OnTimerTick()
        {
            var found = IsNarakaProcessRunning();
            if (found && !IsGameRunning)
            {
                IsGameRunning = true;
                IsLoading = false;
                StatusText = _localizedText.Get("Home.Status.GameStarted", "游戏启动成功");
                StatusHint = _localizedText.Get("Home.Status.GameDetected", "永劫无间进程已检测到");
                if (!_isSubscribed)
                {
                    _isSubscribed = true;
                    _gameLogMonitor.BattleJoined += OnBattleJoined;
                    _gameLogMonitor.BattleStarted += OnBattleStarted;
                    _gameLogMonitor.BattleEnded += OnBattleEnded;
                    try { await _gameLogMonitor.StartAsync(); }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "HomePage", "GameLogMonitor start error");
                    }
                    try { _gameStatusMonitor.Start(); }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "HomePage", "GameStatusMonitor start error");
                    }
                    // StartAsync 若已被 MainWindowVM 调过会早退，本 VM 的订阅器就错过了 replay-snapshot；
                    // 再补发一次，保证本页 UI 与当前对局阶段一致（无活跃对局则不发）。
                    try { _gameLogMonitor.PublishSnapshot(); }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "HomePage", "PublishSnapshot error");
                    }
                }
            }
            else if (!found && IsGameRunning)
            {
                IsGameRunning = false;
                IsLoading = true;
                StatusText = _localizedText.Get("Home.Status.WaitingForGame", "等待游戏启动");
                StatusHint = string.Empty;
                if (_isSubscribed)
                {
                    _isSubscribed = false;
                    _gameLogMonitor.BattleJoined -= OnBattleJoined;
                    _gameLogMonitor.BattleStarted -= OnBattleStarted;
                    _gameLogMonitor.BattleEnded -= OnBattleEnded;
                    _gameLogMonitor.Stop();
                    _gameStatusMonitor.Stop();
                }
            }
        }

        // BattleJoined/Started/Ended 由 GameLogMonitor 在 ThreadPool（OnLogChanged → Task.Run）
        // 上同步触发，直接在后台线程写 StatusHint 会让 WPF 绑定漏刷新——曾出现"对局已结束、
        // 标题栏更新为对局结束，但启动页正文仍停留在'对局中'"的不一致。故 StatusHint 赋值统一
        // marshal 回 UI 线程。NotifyStatus 仍在原线程同步触发，下游 handler 各自负责自己的线程安全。
        private void SetStatusHintOnUi(string hint)
        {
            if (_uiDispatcher.CheckAccess()) { StatusHint = hint; return; }
            _ = _uiDispatcher.InvokeAsync(() => StatusHint = hint);
        }

        private void OnBattleJoined(object? sender, BattleEventArgs args)
        {
            _gameStatusMonitor.NotifyStatus(GameStatus.HeroSelection);
            SetStatusHintOnUi(string.Format(_localizedText.Get("Home.Status.HeroSelection", "英雄选择中 (RoomId: {0})"), args.RoomId));
        }

        private void OnBattleStarted(object? sender, BattleEventArgs args)
        {
            _gameStatusMonitor.NotifyStatus(GameStatus.InGame);
            SetStatusHintOnUi(string.Format(_localizedText.Get("Home.Status.InGame", "对局中 (BattleId: {0})"), args.BattleId));
        }

        private void OnBattleEnded(object? sender, BattleEventArgs args)
        {
            _gameStatusMonitor.NotifyStatus(GameStatus.BattleEnded);
            SetStatusHintOnUi(string.Empty);
        }

        private static bool IsNarakaProcessRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("NarakaBladepoint");
                try
                {
                    return processes.Length > 0;
                }
                finally
                {
                    foreach (var proc in processes) proc.Dispose();
                }
            }
            catch
            {
            }
            return false;
        }

        private void StartProcessCheckLoop()
        {
            _processCheckCts?.Cancel();
            _processCheckCts?.Dispose();
            _processCheckCts = new CancellationTokenSource();
            _ = RunProcessCheckLoopAsync(_processCheckCts.Token);
        }

        private async Task RunProcessCheckLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // marshal 回 UI 线程执行 OnTimerTick；通过 Func<Task> 重载让 OnTimerTick 内部异常能被本方法 try 捕获
                    await _uiDispatcher.InvokeAsync(() => OnTimerTick()).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMs), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
        }

        private void StopProcessCheckLoop()
        {
            _processCheckCts?.Cancel();
            _processCheckCts?.Dispose();
            _processCheckCts = null;
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            StartProcessCheckLoop();
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            StopProcessCheckLoop();
            if (_isSubscribed)
            {
                _isSubscribed = false;
                _gameLogMonitor.BattleJoined -= OnBattleJoined;
                _gameLogMonitor.BattleStarted -= OnBattleStarted;
                _gameLogMonitor.BattleEnded -= OnBattleEnded;
            }
            base.OnNavigatedFromExecute(navigationContext);
        }
    }
}
