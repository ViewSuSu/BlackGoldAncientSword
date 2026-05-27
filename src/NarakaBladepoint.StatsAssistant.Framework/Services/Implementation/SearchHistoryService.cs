using System.Collections.ObjectModel;
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
            // 构造时火后忘却异步加载，不阻塞 UI 线程
            _ = LoadAsync();
        }

        public void Add(string query)
        {
            History.Insert(0, new SearchHistoryItem { Query = query, Timestamp = DateTime.Now });
            if (History.Count > 50) History.RemoveAt(History.Count - 1);
            _ = SaveAsync();
        }

        public async Task DeleteAsync(SearchHistoryItem item)
        {
            History.Remove(item);
            await SaveAsync();
        }

        /// <summary>
        /// 异步加载搜索历史文件。
        /// </summary>
        private async Task LoadAsync()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                if (System.IO.File.Exists(FilePath))
                {
                    var json = await System.IO.File.ReadAllTextAsync(FilePath);
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
