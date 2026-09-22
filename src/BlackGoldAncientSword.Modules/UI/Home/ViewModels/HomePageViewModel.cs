using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.Home.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private const int PollIntervalMs = 2000;
        private const int OverviewPollIntervalMs = 10000;
        private readonly IGameLogMonitor _gameLogMonitor;
        private readonly IGameStatusMonitor _gameStatusMonitor;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly GameOverviewProvider _gameOverview;
        private CancellationTokenSource? _processCheckCts;
        private CancellationTokenSource? _overviewCts;

        public HomePageViewModel(
            IGameLogMonitor gameLogMonitor,
            IGameStatusMonitor gameStatusMonitor,
            IUIDispatcher uiDispatcher,
            ILocalizedTextProvider localizedText,
            GameOverviewProvider gameOverview)
        {
            _gameLogMonitor = gameLogMonitor;
            _gameStatusMonitor = gameStatusMonitor;
            _uiDispatcher = uiDispatcher;
            _localizedText = localizedText;
            _gameOverview = gameOverview;

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

        private UnifiedGameOverview? _overview;
        public UnifiedGameOverview? Overview
        {
            get => _overview;
            private set
            {
                if (ReferenceEquals(_overview, value)) return;
                _overview = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasOverview));
                RaisePropertyChanged(nameof(ScoreCommentText));
                UpdateTrend();
            }
        }

        public bool HasOverview => _overview is not null;

        public string ScoreCommentText =>
            string.IsNullOrEmpty(_overview?.ScoreCommentCount)
                ? string.Empty
                : string.Format(
                    CultureInfo.CurrentCulture,
                    _localizedText.Get("Home.Game.ScoreComments", "{0} 人评价"),
                    _overview!.ScoreCommentCount);

        private IReadOnlyList<TrendBarItem> _trendBars = Array.Empty<TrendBarItem>();
        public IReadOnlyList<TrendBarItem> TrendBars
        {
            get => _trendBars;
            private set
            {
                _trendBars = value;
                RaisePropertyChanged();
            }
        }

        private string _trendPeakText = string.Empty;
        public string TrendPeakText
        {
            get => _trendPeakText;
            private set
            {
                _trendPeakText = value;
                RaisePropertyChanged();
            }
        }

        private void UpdateTrend()
        {
            var points = _overview?.OnlineTrend ?? Array.Empty<UnifiedGameTrendPoint>();
            if (points.Count == 0)
            {
                TrendBars = Array.Empty<TrendBarItem>();
                TrendPeakText = string.Empty;
                return;
            }

            var min = double.MaxValue;
            var max = 0d;
            foreach (var point in points)
            {
                min = Math.Min(min, point.Peak);
                max = Math.Max(max, point.Peak);
            }
            if (max <= 0 || min > max) { min = 0; max = 1; }

            // 柱高压到数据的实际区间里：15 天的峰值本来就挨得很近，若从 0 起算，
            // 所有柱子会长得几乎一样高，看上去像没有数据。区间缩放后走势才看得出来。
            // 具体像素高度交给视图按容器尺寸算（星号权重），窗口拉高时走势跟着长高。
            var span = max - min;
            var bars = new List<TrendBarItem>(points.Count);
            foreach (var point in points)
            {
                var ratio = span > 0 ? (point.Peak - min) / span : 0.5;
                bars.Add(new TrendBarItem
                {
                    Ratio = ratio,
                    ValueText = FormatPeak(point.Peak),
                    DateText = point.Date.ToString("M/d", CultureInfo.CurrentCulture),
                    Tooltip = string.Format(
                        CultureInfo.CurrentCulture, "{0:M月d日}  {1}", point.Date, FormatPeak(point.Peak)),
                });
            }

            TrendBars = bars;
            TrendPeakText = FormatPeak(max);
        }

        private static string FormatPeak(double value)
        {
            if (value >= 10000)
                return (value / 10000).ToString("0.#", CultureInfo.InvariantCulture) + "万";

            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        private async Task RunOverviewPollingAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var overview = await _gameOverview.GetAsync(ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return;

                    if (overview is not null)
                        await _uiDispatcher.InvokeAsync(() => Overview = overview);

                    await Task.Delay(OverviewPollIntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, nameof(HomePageViewModel), "Overview refresh error");
                }
            }
        }

        private void StartOverviewPolling()
        {
            _overviewCts?.Cancel();
            _overviewCts?.Dispose();
            _overviewCts = new CancellationTokenSource();
            _ = RunOverviewPollingAsync(_overviewCts.Token);
        }

        private void StopOverviewPolling()
        {
            _overviewCts?.Cancel();
            _overviewCts?.Dispose();
            _overviewCts = null;
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
            StartOverviewPolling();
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            StopProcessCheckLoop();
            StopOverviewPolling();
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
