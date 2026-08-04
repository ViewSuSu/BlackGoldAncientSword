using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class SearchHistoryService : ISearchHistoryService
    {
        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true
        };

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
            LoadAsync().SafeFireAndForget("SearchHistoryService.LoadAsync");
        }

        public void Add(string query)
        {
            History.Insert(0, new SearchHistoryItem { Query = query, Timestamp = DateTime.Now });
            if (History.Count > 50) History.RemoveAt(History.Count - 1);
            SaveAsync().SafeFireAndForget("SearchHistoryService.SaveAsync");
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
                    var items = JsonSerializer.Deserialize<List<SearchHistoryItem>>(json, _readOptions);
                    if (items != null)
                        foreach (var item in items) History.Add(item);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(SearchHistoryService), "LoadAsync failed");
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(History.ToList(), _writeOptions);
                await System.IO.File.WriteAllTextAsync(FilePath, json);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(SearchHistoryService), "SaveAsync failed");
            }
        }
    }
}
