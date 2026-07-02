using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using BlackGoldAncientSword.Downloader.ViewModels;
using HCMessageBox = HandyControl.Controls.MessageBox;

namespace BlackGoldAncientSword.Downloader.Shell
{
    public partial class DownloadWindow : Window
    {
        public event EventHandler? CancelRequested;
        public event EventHandler? RetryRequested;

        public bool IsCancellable { get; set; } = true;

        private Storyboard? _dotStoryboard;

        public DownloadWindow()
        {
            InitializeComponent();

            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };

            Closing += DownloadWindow_Closing;
            Loaded += (_, _) => StartDotBreathing();
            DataContextChanged += (_, _) => HookViewModel();
        }

        // ============ Phase dot 呼吸动画 ============

        private void StartDotBreathing()
        {
            if (_dotStoryboard != null) return;
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.35,
                Duration = TimeSpan.FromMilliseconds(1200),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, PhaseDot);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
            _dotStoryboard = new Storyboard();
            _dotStoryboard.Children.Add(anim);
            _dotStoryboard.Begin(this, isControllable: true);
            ApplyDotBusy();
        }

        private void HookViewModel()
        {
            if (DataContext is DownloadViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
                ApplyDotBusy();
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadViewModel.IsBusy) or nameof(DownloadViewModel.IsError))
            {
                Dispatcher.BeginInvoke(new Action(ApplyDotBusy));
            }
        }

        private void ApplyDotBusy()
        {
            if (_dotStoryboard == null) return;
            if (DataContext is DownloadViewModel vm && vm.IsBusy && !vm.IsError)
                _dotStoryboard.Resume(this);
            else
                _dotStoryboard.Pause(this);
        }

        // ============ 按钮 ============

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryConfirmCancel())
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryConfirmCancel())
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            RetryRequested?.Invoke(this, EventArgs.Empty);
        }

        // ============ 关窗 ============

        private void DownloadWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_closing) return;
            if (!IsCancellable) return;

            if (DataContext is DownloadViewModel vm && vm.IsError)
            {
                CancelRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!TryConfirmCancel())
            {
                e.Cancel = true;
                return;
            }
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool _closing;

        public void ForceClose()
        {
            _closing = true;
            Close();
        }

        private bool TryConfirmCancel()
        {
            if (!IsCancellable) return false;
            var result = HCMessageBox.Show(
                "是否停止下载？\n\n已下载的临时文件会被立即清除。",
                "停止下载",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }
    }
}
