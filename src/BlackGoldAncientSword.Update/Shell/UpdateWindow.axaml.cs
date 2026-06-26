using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BlackGoldAncientSword.Update.Shell
{
    public partial class UpdateWindow : Window
    {
        public event EventHandler? CancelRequested;

        /// <summary>是否已进入不可取消阶段（覆盖文件 / 重启主程序），由 UpdaterRunner 设置。</summary>
        public bool IsCancellable { get; set; } = true;

        private bool _closing;

        public UpdateWindow()
        {
            InitializeComponent();
            Closing += UpdateWindow_Closing;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // 仅左键 + 非按钮区域时拖动窗口
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private async void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            if (await TryConfirmCancelAsync())
                CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void UpdateWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            // 强制关闭路径（_closing=true 或 不可取消阶段）：直接放行
            if (_closing) return;
            if (!IsCancellable) return;

            e.Cancel = true;
            if (await TryConfirmCancelAsync())
            {
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>给 UpdaterRunner 在最后强制关闭时调用，跳过二次确认。</summary>
        public void ForceClose()
        {
            _closing = true;
            Close();
        }

        private Task<bool> TryConfirmCancelAsync()
        {
            if (!IsCancellable) return Task.FromResult(false);
            return ConfirmDialog.ShowConfirmAsync(
                this,
                "停止更新",
                "是否停止更新？\n\n已下载的临时文件会被清除。",
                okText: "停止",
                cancelText: "继续");
        }
    }
}
