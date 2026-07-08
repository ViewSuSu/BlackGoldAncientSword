using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.Search.ViewModels
{
    public class SearchPageViewModel : ViewModelBase
    {
        private readonly ISearchHistoryService _searchHistory;
        private readonly IClipboardService _clipboard;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly ITipMessageService _tipMessage;
        private readonly SearchDebounceGate _searchDebounce = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                RaisePropertyChanged(nameof(SearchText));
            }
        }

        public ObservableCollection<SearchHistoryItem> SearchHistory =>
            _searchHistory.History;

        public SearchPageViewModel(
            ISearchHistoryService searchHistory,
            IClipboardService clipboard,
            ILocalizedTextProvider localizedText,
            ITipMessageService tipMessage)
        {
            _searchHistory = searchHistory;
            _clipboard = clipboard;
            _localizedText = localizedText;
            _tipMessage = tipMessage;
        }

        private DelegateCommand? _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(() =>
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return;
                if (!_searchDebounce.TryEnter())
                {
                    _tipMessage.ShowError(_localizedText.Get("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }
                _searchHistory.Add(SearchText);
                SearchText = string.Empty;
            });

        private DelegateCommand<SearchHistoryItem>? _copyCommand;
        public DelegateCommand<SearchHistoryItem> CopyCommand =>
            _copyCommand ??= new DelegateCommand<SearchHistoryItem>(item =>
            {
                if (item == null) return;
                _clipboard.TrySetText(item.Query);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(_localizedText.Get("Search.CopySuccess", "复制成功")));
            });
    }
}