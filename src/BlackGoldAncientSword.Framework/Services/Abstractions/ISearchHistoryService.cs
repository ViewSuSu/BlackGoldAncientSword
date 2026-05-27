using System.Collections.ObjectModel;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface ISearchHistoryService
    {
        ObservableCollection<SearchHistoryItem> History { get; }
        void Add(string query);
        System.Threading.Tasks.Task DeleteAsync(SearchHistoryItem item);
    }
}
