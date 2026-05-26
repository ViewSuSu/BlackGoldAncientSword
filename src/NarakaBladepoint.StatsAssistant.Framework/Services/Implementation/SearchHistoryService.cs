using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class SearchHistoryService : ISearchHistoryService
    {
        public ObservableCollection<SearchHistoryItem> History { get; } = new();

        private string FilePath
        {
            get
            {
                var path = AppSettings.GetDefaultPath();
                return System.IO.Path.Combine(path, "search_history.json");
            }
        }

        public SearchHistoryService()
        {
            Load();
        }

        public void Add(string query)
        {
            History.Insert(0, new SearchHistoryItem { Query = query, Timestamp = DateTime.Now });
            if (History.Count > 50) History.RemoveAt(History.Count - 1);
            _ = Task.Run(SaveAsync);
        }

        public async Task DeleteAsync(SearchHistoryItem item)
        {
            History.Remove(item);
            await SaveAsync();
        }

        private void Load()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                if (System.IO.File.Exists(FilePath))
                {
                    var json = System.IO.File.ReadAllText(FilePath);
                    var items = JsonConvert.DeserializeObject<List<SearchHistoryItem>>(json);
                    if (items != null)
                        foreach (var item in items) History.Add(item);
                }
            }
            catch { }
        }

        private async Task SaveAsync()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(History.ToList(), Formatting.Indented);
                await System.IO.File.WriteAllTextAsync(FilePath, json);
            }
            catch { }
        }
    }
}