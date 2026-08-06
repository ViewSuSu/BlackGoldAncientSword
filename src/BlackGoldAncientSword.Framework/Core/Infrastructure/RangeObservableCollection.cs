using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// 支持批量替换的 <see cref="ObservableCollection{T}"/>。
    /// <see cref="ReplaceAll"/> 只触发一次 Reset 通知，避免逐条 Add 时 WPF 列表反复
    /// 测量/排版造成 UI 卡顿（战绩页一次加载约 50 条对局时尤为明显）。
    /// </summary>
    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>清空并用 <paramref name="items"/> 重填，全程只发一次 CollectionChanged(Reset)。</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
