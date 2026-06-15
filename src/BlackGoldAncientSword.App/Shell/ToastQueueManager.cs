using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Events;

namespace BlackGoldAncientSword.App.Shell
{
    /// <summary>
    /// Toast 通知队列管理器：订阅 <see cref="TipMessageEvent"/>，把消息追加到可观察集合，
    /// 供 MainWindow 的 ItemsControl 绑定显示；UI 层的淡入/淡出动画结束后从集合中移除。
    /// 单例存在，避免 VM 重建导致集合引用失效。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class ToastQueueManager
    {
        /// <summary>Toast 项集合，绑定到 XAML 的 ItemsControl.ItemsSource。</summary>
        public ObservableCollection<ToastItem> Items { get; } = new();

        public ToastQueueManager(IEventAggregator eventAggregator)
        {
            eventAggregator.GetEvent<TipMessageEvent>()
                .Subscribe(OnTipMessage, ThreadOption.UIThread);
        }

        private void OnTipMessage(TipMessageWithHighlightArgs args)
        {
            Items.Add(new ToastItem
            {
                Message = args.Message,
                IsError = args.HighlightTexts.Contains("Error"),
            });
        }
    }

    /// <summary>
    /// 单条 Toast 项。原本定义在 MainWindowViewModel.cs，随 Toast 逻辑一起搬迁。
    /// 命名空间保持 BlackGoldAncientSword.App.Shell，避免 XAML 与 code-behind 绑定路径变更。
    /// </summary>
    public class ToastItem : ViewModelBase
    {
        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set
            {
                if (_message == value) return;
                _message = value;
                RaisePropertyChanged(nameof(Message));
            }
        }

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set
            {
                if (_isError == value) return;
                _isError = value;
                RaisePropertyChanged(nameof(IsError));
            }
        }
    }
}
