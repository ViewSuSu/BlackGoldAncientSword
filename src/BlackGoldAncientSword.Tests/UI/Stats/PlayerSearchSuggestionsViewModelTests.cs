using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.Stats.ViewModels;

namespace BlackGoldAncientSword.Tests.UI.Stats
{
    /// <summary>
    /// 搜索框候选列表的分页契约：一次只上屏一页、滚动追加时按角色 ID 去重、
    /// 关键词换了以后旧结果回来必须作废（候选只反映当前关键词）。
    /// 假的数据源直接给出候选，不打接口。
    /// </summary>
    public class PlayerSearchSuggestionsViewModelTests
    {
        private sealed class ImmediateDispatcher : IUIDispatcher
        {
            public bool CheckAccess() => true;
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
            public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
            public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
            public void BeginInvoke(Action action) => action();
        }

        /// <summary>
        /// 把"取数落地"这一次上屏卡住：动作已经执行完（loading 标志都落好了），但发起方的锁还没释放。
        /// 用来复现"锁已放开标志、未释放锁"这个真实存在的窄窗口。
        /// </summary>
        private sealed class GatedDispatcher : IUIDispatcher
        {
            private readonly TaskCompletionSource _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _applied;

            public bool CheckAccess() => true;

            public Task InvokeAsync(Action action)
            {
                action();
                return ++_applied >= 2 ? _hold.Task : Task.CompletedTask;
            }

            public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
            public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
            public void BeginInvoke(Action action) => action();

            public void Release() => _hold.TrySetResult();
        }

        private static PlayerSearchSuggestionsViewModel BuildViewModel(
            Func<string, int, int, CancellationToken, Task<List<UnifiedSearchResult>>> fetch)
            => new(new ImmediateDispatcher(), fetch);

        private static List<UnifiedSearchResult> Page(string prefix, int count, int firstIndex = 0)
            => Enumerable.Range(firstIndex, count)
                .Select(i => new UnifiedSearchResult
                {
                    RoleIdSimple = $"{prefix}-{i}",
                    RoleName = $"{prefix}{i}",
                    Server = "163",
                })
                .ToList();

        [Fact]
        public async Task Restart_Should_Open_And_Show_First_Page()
        {
            var calls = new List<(string Keyword, int Offset, int Limit)>();
            var vm = BuildViewModel((keyword, offset, limit, _) =>
            {
                calls.Add((keyword, offset, limit));
                return Task.FromResult(Page("p", PlayerSearchSuggestionsViewModel.PageSize));
            });

            await vm.RestartAsync("  小窗  ");

            Assert.True(vm.IsOpen);
            Assert.False(vm.IsLoading);
            Assert.Equal(PlayerSearchSuggestionsViewModel.PageSize, vm.Items.Count);
            Assert.True(vm.HasMore);
            Assert.Equal(0, vm.SelectedIndex);
            var call = Assert.Single(calls);
            Assert.Equal("小窗", call.Keyword);
            Assert.Equal(0, call.Offset);
            Assert.Equal(PlayerSearchSuggestionsViewModel.PageSize, call.Limit);
        }

        [Fact]
        public async Task Restart_Should_Put_Rank_Uid_And_Every_Other_Field_On_The_Item()
        {
            var vm = BuildViewModel((_, _, _, _) => Task.FromResult(new List<UnifiedSearchResult>
            {
                new()
                {
                    RoleIdSimple = "uipe000001677200140163",
                    RoleName = "小窗",
                    Server = "163",
                    LevelName = "白银Ⅳ",
                    LevelImg = "https://img/rank.png",
                    Fields = new[] { new UnifiedSearchField { Title = "积分", Text = "1600" } },
                },
            }));

            await vm.RestartAsync("小窗");

            var item = Assert.Single(vm.Items);
            Assert.Equal("https://img/rank.png", item.RankIconUrl);
            // 段位单独占一列摆在行右侧，不再混在第二行文案里。
            Assert.Equal("白银Ⅳ", item.RankName);
            Assert.True(item.HasRank);
            Assert.Equal("UID uipe000001677200140163 · 积分 1600", item.MetaText);
        }

        /// <summary>接口没给段位时不留一块空位。</summary>
        [Fact]
        public async Task Restart_Should_Mark_Item_Without_Rank_As_Having_None()
        {
            var vm = BuildViewModel((_, _, _, _) => Task.FromResult(new List<UnifiedSearchResult>
            {
                new() { RoleIdSimple = "uipe000001677200140163", RoleName = "小窗", Server = "163" },
            }));

            await vm.RestartAsync("小窗");

            var item = Assert.Single(vm.Items);
            Assert.False(item.HasRank);
            Assert.Equal("UID uipe000001677200140163", item.MetaText);
        }

        [Fact]
        public async Task Restart_Should_Stop_Paging_When_Page_Is_Short()
        {
            var vm = BuildViewModel((keyword, _, _, _) => Task.FromResult(Page(keyword, 3)));

            await vm.RestartAsync("kw");

            Assert.Equal(3, vm.Items.Count);
            Assert.False(vm.HasMore);

            await vm.LoadMoreAsync();

            Assert.Equal(3, vm.Items.Count);
        }

        [Fact]
        public async Task Restart_Should_Report_No_Result_When_Nothing_Matched()
        {
            var vm = BuildViewModel((_, _, _, _) => Task.FromResult(new List<UnifiedSearchResult>()));

            await vm.RestartAsync("kw");

            Assert.True(vm.IsOpen);
            Assert.True(vm.HasNoResult);
            Assert.Empty(vm.Items);
            Assert.Equal(-1, vm.SelectedIndex);
        }

        [Fact]
        public async Task Restart_With_Blank_Keyword_Should_Not_Fetch_And_Close()
        {
            var fetches = 0;
            var vm = BuildViewModel((_, _, _, _) =>
            {
                fetches++;
                return Task.FromResult(Page("p", 1));
            });

            await vm.RestartAsync("   ");

            Assert.Equal(0, fetches);
            Assert.False(vm.IsOpen);
            Assert.Empty(vm.Items);
        }

        [Fact]
        public async Task LoadMore_Should_Append_Next_Page_And_Skip_Duplicates()
        {
            const int pageSize = PlayerSearchSuggestionsViewModel.PageSize;
            var offsets = new List<int>();
            var vm = BuildViewModel((keyword, offset, _, _) =>
            {
                offsets.Add(offset);
                // 第二页故意与第一页重叠 5 条（15..19），用于验证按角色 ID 去重。
                return Task.FromResult(offset == 0 ? Page("p", pageSize) : Page("p", pageSize, firstIndex: pageSize - 5));
            });

            await vm.RestartAsync("kw");
            await vm.LoadMoreAsync();

            Assert.Equal(new[] { 0, pageSize }, offsets);
            // 第二页 20 条里有 5 条与第一页重复，只应新增 15 条。
            Assert.Equal(2 * pageSize - 5, vm.Items.Count);
            Assert.Equal(vm.Items.Count, vm.Items.Select(i => i.RoleId).Distinct().Count());
            Assert.False(vm.IsLoadingMore);
        }

        [Fact]
        public async Task Restart_Should_Reissue_Latest_Keyword_And_Drop_Superseded_Result()
        {
            var pending = new TaskCompletionSource<List<UnifiedSearchResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var call = 0;
            var vm = BuildViewModel((keyword, _, _, _) =>
            {
                call++;
                return call == 1 ? pending.Task : Task.FromResult(Page("new", 2));
            });

            var stale = vm.RestartAsync("old");
            await vm.RestartAsync("new");

            // 旧关键词还在飞：新关键词只登记不发起，绝不并发第二个请求。
            Assert.Equal(1, call);
            Assert.Empty(vm.Items);

            // 旧请求回来后补发最新关键词，最终上屏的是新关键词的候选。
            pending.SetResult(Page("old", 2));
            await stale;

            Assert.Equal(new[] { "new-0", "new-1" }, vm.Items.Select(i => i.RoleId).ToArray());
        }

        [Fact]
        public async Task Close_Should_Drop_InFlight_Result()
        {
            var pending = new TaskCompletionSource<List<UnifiedSearchResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = BuildViewModel((_, _, _, _) => pending.Task);

            var inFlight = vm.RestartAsync("kw");
            vm.Close();
            pending.SetResult(Page("p", 2));
            await inFlight;

            Assert.False(vm.IsOpen);
            Assert.Empty(vm.Items);
        }

        /// <summary>
        /// 一次取数刚落地（loading 标志已复位）但锁还没放开时，滚动追加要被挡下。
        /// 挡下可以，但不能把"正在加载更多"留在原地——否则这个关键词再也追加不上。
        /// </summary>
        [Fact]
        public async Task LoadMore_Should_Not_Stay_Loading_When_The_Lock_Is_Still_Busy()
        {
            var dispatcher = new GatedDispatcher();
            var calls = 0;
            var vm = new PlayerSearchSuggestionsViewModel(dispatcher, (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult(Page("p", PlayerSearchSuggestionsViewModel.PageSize));
            });

            var restart = vm.RestartAsync("kw");
            await vm.LoadMoreAsync();

            Assert.Equal(1, calls);
            Assert.False(vm.IsLoadingMore);

            // 锁放开：本次追加没发出去，交给下一次滚动重新触发。
            dispatcher.Release();
            await restart;

            await vm.LoadMoreAsync();

            Assert.Equal(2, calls);
            Assert.False(vm.IsLoadingMore);
        }
    }
}
