using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BlackGoldAncientSword.Update.ViewModels
{
    public sealed class UpdateViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>0..100。下载阶段占 0..90，解压+覆盖占 90..100。</summary>
        private double _percent;
        public double Percent
        {
            get => _percent;
            set
            {
                if (Math.Abs(_percent - value) < 0.001) return;
                _percent = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PercentText));
            }
        }

        public string PercentText => $"{Percent:0}%";

        private string _statusText = "准备中...";
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                RaisePropertyChanged();
            }
        }

        private string _speedText = string.Empty;
        public string SpeedText
        {
            get => _speedText;
            set
            {
                if (_speedText == value) return;
                _speedText = value;
                RaisePropertyChanged();
            }
        }

        private string _sizeText = string.Empty;
        public string SizeText
        {
            get => _sizeText;
            set
            {
                if (_sizeText == value) return;
                _sizeText = value;
                RaisePropertyChanged();
            }
        }

        private string _etaText = string.Empty;
        public string EtaText
        {
            get => _etaText;
            set
            {
                if (_etaText == value) return;
                _etaText = value;
                RaisePropertyChanged();
            }
        }

        private bool _showForceClose;
        public bool ShowForceClose
        {
            get => _showForceClose;
            set
            {
                if (_showForceClose == value) return;
                _showForceClose = value;
                RaisePropertyChanged();
            }
        }

        private string _runningProcessText = string.Empty;
        public string RunningProcessText
        {
            get => _runningProcessText;
            set
            {
                if (_runningProcessText == value) return;
                _runningProcessText = value;
                RaisePropertyChanged();
            }
        }

        private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
