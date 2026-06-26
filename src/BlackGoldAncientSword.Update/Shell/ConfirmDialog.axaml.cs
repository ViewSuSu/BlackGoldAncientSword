using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BlackGoldAncientSword.Update.Shell
{
    public partial class ConfirmDialog : Window
    {
        private readonly TaskCompletionSource<bool> _tcs = new();

        public ConfirmDialog()
        {
            InitializeComponent();
            Closed += (_, _) => _tcs.TrySetResult(false);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(true);
            Close();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(false);
            Close();
        }

        /// <summary>
        /// 弹出二选一确认对话框。返回 true=点击 OK，false=点击 Cancel/关闭。
        /// </summary>
        public static Task<bool> ShowConfirmAsync(
            Window? owner,
            string title,
            string message,
            string okText = "确定",
            string cancelText = "取消")
        {
            var dlg = new ConfirmDialog
            {
                Title = title,
            };
            dlg.SetText(title, message);
            dlg.SetButtons(okText, cancelText, showCancel: true);

            if (owner != null)
            {
                _ = dlg.ShowDialog(owner);
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Show();
            }
            return dlg._tcs.Task;
        }

        /// <summary>
        /// 弹出仅 OK 的提示对话框（用于错误显示）。
        /// </summary>
        public static Task ShowErrorAsync(Window? owner, string title, string message)
        {
            var dlg = new ConfirmDialog
            {
                Title = title,
            };
            dlg.SetText(title, message);
            dlg.SetButtons("确定", string.Empty, showCancel: false);

            if (owner != null)
            {
                _ = dlg.ShowDialog(owner);
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Show();
            }
            return dlg._tcs.Task;
        }

        private void SetText(string title, string message)
        {
            if (this.FindControl<TextBlock>("TitleText") is { } t) t.Text = title;
            if (this.FindControl<TextBlock>("MessageText") is { } m) m.Text = message;
        }

        private void SetButtons(string okText, string cancelText, bool showCancel)
        {
            if (this.FindControl<Button>("OkButton") is { } ok) ok.Content = okText;
            if (this.FindControl<Button>("CancelButton") is { } cancel)
            {
                cancel.Content = cancelText;
                cancel.IsVisible = showCancel;
            }
        }
    }
}
