using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.Search.ViewModels
{
    public class SearchPageViewModel : ViewModelBase
    {
        private readonly ISearchHistoryService _searchHistory;
        private readonly IClipboardService _clipboard;
        private readonly ILocalizedTextProvider _localizedText;

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
            ILocalizedTextProvider localizedText)
        {
            _searchHistory = searchHistory;
            _clipboard = clipboard;
            _localizedText = localizedText;
        }

        private DelegateCommand? _searchCommand;
        public DelegateCommand SearchCommand =>
            _searchCommand ??= new DelegateCommand(() =>
            {
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    _searchHistory.Add(SearchText);
                    SearchText = string.Empty;
                }
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