using System;
using System.Diagnostics;
using System.Windows.Threading;
using NarakaBladepoint.StatsAssistant.GameMonitor.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Modules.UI.Home.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private const int PollIntervalMs = 2000;
        private readonly DispatcherTimer _processTimer;
        private readonly IGameLogMonitor _gameLogMonitor;

        public HomePageViewModel(IGameLogMonitor gameLogMonitor)
        {
            _gameLogMonitor = gameLogMonitor;
            _processTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _processTimer.Tick += OnTimerTick;

            StatusText = "等待客户端连接";
            IsLoading = true;
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _statusHint = string.Empty;
        public string StatusHint
        {
            get => _statusHint;
            set => SetProperty(ref _statusHint, value);
        }

        private bool _isGameRunning;
        public bool IsGameRunning
        {
            get => _isGameRunning;
            set => SetProperty(ref _isGameRunning, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _monitorStarted;
        private void OnTimerTick(object? sender, EventArgs e)
        {
            var found = IsNarakaProcessRunning();
            if (found && !IsGameRunning)
            {
                IsGameRunning = true;
                IsLoading = false;
                StatusText = "游戏启动成功";
                StatusHint = "永劫无间进程已检测到";
                if (!_monitorStarted)
                {
                    _monitorStarted = true;
                    _ = _gameLogMonitor.StartAsync();
                }
            }
            else if (!found && IsGameRunning)
            {
                IsGameRunning = false;
                IsLoading = true;
                StatusText = "等待客户端连接";
                StatusHint = string.Empty;
                if (_monitorStarted)
                {
                    _monitorStarted = false;
                    _gameLogMonitor.Stop();
                }
            }
        }

        private static bool IsNarakaProcessRunning()
        {
            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.ProcessName.Equals("NarakaBladepoint", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            _processTimer.Start();
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            _processTimer.Stop();
            base.OnNavigatedFromExecute(navigationContext);
        }
    }
}
