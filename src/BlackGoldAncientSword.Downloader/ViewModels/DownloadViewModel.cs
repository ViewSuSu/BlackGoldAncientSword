using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BlackGoldAncientSword.Downloader.ViewModels
{
    /// <summary>
    /// 下载器 UI 状态。围绕专业下载器共性布局（VS Installer / Steam / GitHub Desktop）设计：
    ///   - 品牌栏：VersionTag
    ///   - Phase chip：PhaseText + FileIndex/FileTotal
    ///   - 主进度：Percent + IsIndeterminate（连接/安装阶段切换）
    ///   - 副进度：CurrentFileName + CurrentFilePercent
    ///   - 4-stat grid：SpeedText / DownloadedText / TotalSizeText / EtaText
    ///   - 底部行动区：BottomHintText + IsCancelEnabled
    ///   - 错误面板：IsError + ErrorMessage（替换中央区）
    /// 全部属性变更走 RaisePropertyChanged([CallerMemberName])，遵守 CLAUDE.md ViewModel 规则；
    /// ViewModel 中不引用任何 WPF 类型（可见性由 Converter 转 Visibility）。
    /// </summary>
    public sealed class DownloadViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // ============ 品牌栏 ============

        private string _versionTag = string.Empty;
        public string VersionTag
        {
            get => _versionTag;
            set { if (_versionTag == value) return; _versionTag = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasVersionTag)); }
        }

        public bool HasVersionTag => !string.IsNullOrEmpty(_versionTag);

        // ============ Phase chip ============

        private string _phaseText = "准备中";
        public string PhaseText
        {
            get => _phaseText;
            set { if (_phaseText == value) return; _phaseText = value; RaisePropertyChanged(); }
        }

        private int _fileIndex;
        public int FileIndex
        {
            get => _fileIndex;
            set { if (_fileIndex == value) return; _fileIndex = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(FileIndexText)); RaisePropertyChanged(nameof(HasFileIndex)); }
        }

        private int _fileTotal;
        public int FileTotal
        {
            get => _fileTotal;
            set { if (_fileTotal == value) return; _fileTotal = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(FileIndexText)); RaisePropertyChanged(nameof(HasFileIndex)); }
        }

        public string FileIndexText => _fileTotal > 0 ? $"{_fileIndex} / {_fileTotal}" : string.Empty;
        public bool HasFileIndex => _fileTotal > 0;

        /// <summary>Phase dot 呼吸动画开关（仅下载中）。IsIndeterminate 或 IsError 时应关。</summary>
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy == value) return; _isBusy = value; RaisePropertyChanged(); }
        }

        // ============ 主进度 ============

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

        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set { if (_isIndeterminate == value) return; _isIndeterminate = value; RaisePropertyChanged(); }
        }

        // ============ 副进度：当前文件 ============

        private string _currentFileName = string.Empty;
        public string CurrentFileName
        {
            get => _currentFileName;
            set { if (_currentFileName == value) return; _currentFileName = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasCurrentFile)); }
        }

        public bool HasCurrentFile => !string.IsNullOrEmpty(_currentFileName);

        private double _currentFilePercent;
        public double CurrentFilePercent
        {
            get => _currentFilePercent;
            set { if (Math.Abs(_currentFilePercent - value) < 0.001) return; _currentFilePercent = value; RaisePropertyChanged(); }
        }

        // ============ 4-stat grid ============

        private string _speedText = "—";
        public string SpeedText
        {
            get => _speedText;
            set { if (_speedText == value) return; _speedText = value; RaisePropertyChanged(); }
        }

        private string _downloadedText = "—";
        public string DownloadedText
        {
            get => _downloadedText;
            set { if (_downloadedText == value) return; _downloadedText = value; RaisePropertyChanged(); }
        }

        private string _totalSizeText = "—";
        public string TotalSizeText
        {
            get => _totalSizeText;
            set { if (_totalSizeText == value) return; _totalSizeText = value; RaisePropertyChanged(); }
        }

        private string _etaText = "—";
        public string EtaText
        {
            get => _etaText;
            set { if (_etaText == value) return; _etaText = value; RaisePropertyChanged(); }
        }

        // ============ 底部行动区 ============

        private string _bottomHintText = "准备开始下载...";
        public string BottomHintText
        {
            get => _bottomHintText;
            set { if (_bottomHintText == value) return; _bottomHintText = value; RaisePropertyChanged(); }
        }

        private bool _isCancelEnabled = true;
        public bool IsCancelEnabled
        {
            get => _isCancelEnabled;
            set { if (_isCancelEnabled == value) return; _isCancelEnabled = value; RaisePropertyChanged(); }
        }

        // ============ 错误态 ============

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set { if (_isError == value) return; _isError = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(IsNormal)); }
        }

        /// <summary>非错误态 = 正常展示中央下载信息区。用于 Visibility 反向绑定。</summary>
        public bool IsNormal => !_isError;

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { if (_errorMessage == value) return; _errorMessage = value; RaisePropertyChanged(); }
        }

        private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
