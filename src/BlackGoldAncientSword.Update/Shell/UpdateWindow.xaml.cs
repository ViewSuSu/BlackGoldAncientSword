using System;
using System.Windows;
using System.Windows.Input;

namespace BlackGoldAncientSword.Update.Shell
{
    public partial class UpdateWindow : Window
    {
        public event EventHandler? CancelRequested;

        /// <summary>是否已进入不可取消阶段（覆盖文件 / 重启主程序），由 UpdaterRunner 设置。</summary>
        public bool IsCancellable { get; set; } = true;

        public UpdateWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) DragMove();
            };
            Closing += UpdateWindow_Closing;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryConfirmCancel())
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Alt+F4 / 系统关闭也走同样的二次确认
            if (!IsCancellable) return;
            if (_closing) return;
            if (!TryConfirmCancel())
            {
                e.Cancel = true;
                return;
            }
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool _closing;

        /// <summary>给 UpdaterRunner 在最后强制关闭时调用，跳过二次确认。</summary>
        public void ForceClose()
        {
            _closing = true;
            Close();
        }

        private bool TryConfirmCancel()
        {
            if (!IsCancellable) return false;
            var result = MessageBox.Show(
                this,
                "是否停止更新？\n\n已下载的临时文件会被清除。",
                "停止更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }
    }
}
