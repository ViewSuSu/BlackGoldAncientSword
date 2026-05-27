using System.Collections.ObjectModel;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.Search.ViewModels
{
    public class SearchPageViewModel : ViewModelBase
    {
        private readonly ISearchHistoryService _searchHistory;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public ObservableCollection<SearchHistoryItem> SearchHistory =>
            _searchHistory.History;

        public SearchPageViewModel(ISearchHistoryService searchHistory)
        {
            _searchHistory = searchHistory;
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
                Clipboard.SetText(item.Query);
                eventAggregator.GetEvent<TipMessageEvent>()
                    .Publish(new TipMessageWithHighlightArgs(Application.Current?.TryFindResource("Search.CopySuccess") as string ?? "复制成功"));
            });
    }
}