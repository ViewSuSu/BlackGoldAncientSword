using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.Stats.ViewModels
{
    /// <summary>
    /// 搜索框候选列表：按关键词取第一页，滚动到底再追加下一页（每页 <see cref="PageSize"/> 条，
    /// 不一次性拉全量）。
    /// <para>
    /// 同一时刻只允许一次取数在飞，第一页与追加页共用同一把锁：在飞期间到达的新关键词不发起请求，
    /// 只记下最新那个，由在飞的那次回来顺路补发。连打不会在排队层堆起一串请求。
    /// </para>
    /// <para>
    /// 另外三处刻意的取舍：
    /// <list type="bullet">
    ///   <item>候选用 <see cref="RangeObservableCollection{T}.ReplaceAll"/> 一次性上屏，只发一次集合通知，
    ///   避免逐条 Add 触发多次列表布局与容器生成；</item>
    ///   <item>每次关键词变化递增 generation，在飞的结果回来时若已被更新的关键词取代就直接丢弃，
    ///   不会出现"旧关键词的候选盖住新关键词的"；</item>
    ///   <item>下一页起点按每页条数累加，不按本地已渲染的条数推算；跨页按角色 ID 去重，
    ///   追加时也不重复渲染同一个人。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class PlayerSearchSuggestionsViewModel : BindableBase
    {
        public const int PageSize = 20;

        private readonly IUIDispatcher _uiDispatcher;
        private readonly Func<string, int, int, CancellationToken, Task<List<UnifiedSearchResult>>> _fetch;

        /// <summary>查询当前账号是否绑定过角色：true/false = 明确结论，null = 没查成（无从判断）。</summary>
        private readonly Func<CancellationToken, Task<bool?>> _accountBoundRoleProbe;

        private readonly HashSet<string> _seenRoleIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前账号是否绑定过角色：null = 还没查到；false = 明确未绑定（空态引导据此显示）。</summary>
        private bool? _accountHasBoundRole;

        /// <summary>本会话是否已发起过绑定状态查询（懒触发一次，避免每次空态都打请求）。</summary>
        private bool _boundRoleCheckStarted;

        private int _generation;
        private string _query = string.Empty;

        /// <summary>下一页的起始位置；关键词变化时归零，每成功取回一页累加一页。</summary>
        private int _nextOffset;

        /// <summary>0 = 空闲，1 = 有一次取数在飞。</summary>
        private int _searchLock;

        /// <summary>在飞期间被挡下的最新关键词，由在飞的那次回来顺路补发。</summary>
        private string? _pendingKeyword;

        public PlayerSearchSuggestionsViewModel(
            IUIDispatcher uiDispatcher,
            Func<string, int, int, CancellationToken, Task<List<UnifiedSearchResult>>> fetch,
            Func<CancellationToken, Task<bool?>>? accountBoundRoleProbe = null)
        {
            _uiDispatcher = uiDispatcher;
            _fetch = fetch;
            // 不接探测（测试 / 其它宿主）时，绑定状态恒为"未知"——空态不显示绑定引导。
            _accountBoundRoleProbe = accountBoundRoleProbe ?? (_ => Task.FromResult<bool?>(null));
        }

        public RangeObservableCollection<PlayerSearchSuggestionItem> Items { get; } = new();

        private bool _isOpen;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen == value) return;
                _isOpen = value;
                RaisePropertyChanged(nameof(IsOpen));
                NotifyNoResultState();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
                NotifyNoResultState();
            }
        }

        private bool _isLoadingMore;
        public bool IsLoadingMore
        {
            get => _isLoadingMore;
            set
            {
                if (_isLoadingMore == value) return;
                _isLoadingMore = value;
                RaisePropertyChanged(nameof(IsLoadingMore));
            }
        }

        private bool _hasMore;
        public bool HasMore
        {
            get => _hasMore;
            set
            {
                if (_hasMore == value) return;
                _hasMore = value;
                RaisePropertyChanged(nameof(HasMore));
            }
        }

        private int _selectedIndex = -1;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                RaisePropertyChanged(nameof(SelectedIndex));
            }
        }

        /// <summary>当前关键词已经搜过、但一条候选都没有（用于显示"没有匹配的玩家"）。</summary>
        public bool HasNoResult => IsOpen && !IsLoading && Items.Count == 0;

        /// <summary>
        /// 空态里的「试试绑定角色」引导是否显示：只在**确认当前账号没绑定过角色**时出现。
        /// 已绑定 → 不显示；还没查成 / 查询失败 → 也不显示（拿不准就不引导）。
        /// </summary>
        public bool ShowTryBindLink => HasNoResult && _accountHasBoundRole == false;

        /// <summary>绑定成功时调用：此后空态不再显示绑定引导。</summary>
        public void MarkAccountBound()
        {
            _accountHasBoundRole = true;
            _boundRoleCheckStarted = true;
            RaisePropertyChanged(nameof(ShowTryBindLink));
        }

        /// <summary>统一发布空态相关的属性通知（探测触发不在这里——见空结果落地处与 ShowNotFound）。</summary>
        private void NotifyNoResultState()
        {
            RaisePropertyChanged(nameof(HasNoResult));
            RaisePropertyChanged(nameof(ShowTryBindLink));
        }

        private void EnsureAccountBoundRoleChecked()
        {
            if (_boundRoleCheckStarted) return;
            _boundRoleCheckStarted = true;
            _ = CheckAccountBoundRoleAsync();
        }

        private async Task CheckAccountBoundRoleAsync()
        {
            try
            {
                var hasBound = await _accountBoundRoleProbe(CancellationToken.None).ConfigureAwait(false);
                await _uiDispatcher.InvokeAsync(() =>
                {
                    _accountHasBoundRole = hasBound;
                    RaisePropertyChanged(nameof(ShowTryBindLink));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 没查成就保持"没查成"（不显示引导）；本会话不再重试——绑定成功的正向路径会直接置为已绑定。
                AppLog.Error(ex, nameof(PlayerSearchSuggestionsViewModel), "query account bound role state failed");
            }
        }

        /// <summary>关键词变化后重新取第一页。旧的候选与在飞结果一并作废。</summary>
        public async Task RestartAsync(string? keyword)
        {
            var query = (keyword ?? string.Empty).Trim();
            var generation = Interlocked.Increment(ref _generation);

            if (query.Length == 0)
            {
                Volatile.Write(ref _pendingKeyword, null);
                await ApplyAsync(generation, () =>
                {
                    _query = string.Empty;
                    ResetItems();
                    IsOpen = false;
                    IsLoading = false;
                }).ConfigureAwait(false);
                return;
            }

            await ApplyAsync(generation, () =>
            {
                _query = query;
                ResetItems();
                IsOpen = true;
                IsLoading = true;
            }).ConfigureAwait(false);

            await RunAsync(generation, query, 0, append: false).ConfigureAwait(false);
        }

        /// <summary>滚动接近底部时追加下一页。已在加载或没有下一页时直接返回。</summary>
        public async Task LoadMoreAsync()
        {
            if (!IsOpen || IsLoading || IsLoadingMore || !HasMore) return;

            var query = _query;
            var offset = _nextOffset;
            if (query.Length == 0 || offset == 0) return;

            var generation = Volatile.Read(ref _generation);
            IsLoadingMore = true;

            await RunAsync(generation, query, offset, append: true).ConfigureAwait(false);
        }

        /// <summary>
        /// 真正发起一次取数。拿不到锁说明已有一次在飞：追加直接放弃（把标记交回给下一次滚动，
        /// 否则这一页再也追加不上），换关键词则记下最新那个，由在飞的那次回来顺路补发，
        /// 保证输入框里的词最终一定被搜到。
        /// </summary>
        private async Task RunAsync(int generation, string query, int offset, bool append)
        {
            if (Interlocked.CompareExchange(ref _searchLock, 1, 0) != 0)
            {
                if (append)
                {
                    IsLoadingMore = false;
                }
                else
                {
                    Volatile.Write(ref _pendingKeyword, query);
                }
                return;
            }

            try
            {
                while (true)
                {
                    var page = await FetchSafeAsync(query, offset, PageSize).ConfigureAwait(false);

                    await ApplyAsync(generation, () =>
                    {
                        if (append)
                        {
                            AppendDedup(page);
                        }
                        else
                        {
                            Items.ReplaceAll(ToItems(page));
                            SelectedIndex = Items.Count > 0 ? 0 : -1;

                            // "搜过之后确实没有候选"才是空态出现的时点（开下拉的瞬间 HasNoResult 也会短暂为真，
                            // 但那是加载态的一帧，不是搜索结果）——绑定状态探测只在这里懒触发。
                            if (Items.Count == 0) EnsureAccountBoundRoleChecked();
                        }

                        _nextOffset = offset + PageSize;
                        HasMore = page.Count >= PageSize;
                        IsLoading = false;
                        IsLoadingMore = false;
                        NotifyNoResultState();
                    }).ConfigureAwait(false);

                    var pending = Volatile.Read(ref _pendingKeyword);
                    if (pending is null) return;

                    Volatile.Write(ref _pendingKeyword, null);
                    query = pending;
                    offset = 0;
                    append = false;
                    generation = Volatile.Read(ref _generation);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _searchLock, 0);
            }
        }

        /// <summary>关闭候选（选中某人、按 Esc、拉到空结果以外的情况）：作废在飞结果并清空。</summary>
        public void Close()
        {
            Interlocked.Increment(ref _generation);
            Volatile.Write(ref _pendingKeyword, null);
            _query = string.Empty;
            ResetItems();
            IsOpen = false;
            IsLoading = false;
            RaisePropertyChanged(nameof(HasNoResult));
        }

        /// <summary>
        /// 手动搜索（回车 / 点搜索）确认的玩家不存在：把下拉以空态形式打开
        /// （复用 <see cref="HasNoResult"/> 的提示 UI），不发起任何新请求。
        /// 页面数据不在这里动——调用方保持原样展示。
        /// </summary>
        public void ShowNotFound()
        {
            var generation = Interlocked.Increment(ref _generation);
            Volatile.Write(ref _pendingKeyword, null);
            _ = ApplyAsync(generation, () =>
            {
                _query = string.Empty;
                ResetItems();
                IsLoading = false;
                IsOpen = true;

                // 手动搜索确认无结果：这也是"空态出现"，同样触发一次绑定状态探测（懒触发、有守卫）。
                EnsureAccountBoundRoleChecked();
            });
        }

        private void ResetItems()
        {
            _seenRoleIds.Clear();
            _nextOffset = 0;
            IsLoadingMore = false;
            HasMore = false;
            SelectedIndex = -1;
            Items.ReplaceAll(Array.Empty<PlayerSearchSuggestionItem>());
        }

        private async Task ApplyAsync(int generation, Action action)
        {
            if (generation != Volatile.Read(ref _generation)) return;
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                action();
            }).ConfigureAwait(false);
        }

        private async Task<List<UnifiedSearchResult>> FetchSafeAsync(string query, int offset, int limit)
        {
            try
            {
                return await _fetch(query, offset, limit, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new List<UnifiedSearchResult>();
            }
            catch (NarakaApiException ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerSearchSuggestionsViewModel)}", "候选搜索被拒绝");
                return new List<UnifiedSearchResult>();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(PlayerSearchSuggestionsViewModel)}.{nameof(FetchSafeAsync)}");
                return new List<UnifiedSearchResult>();
            }
        }

        private List<PlayerSearchSuggestionItem> ToItems(List<UnifiedSearchResult> page)
        {
            _seenRoleIds.Clear();
            var items = new List<PlayerSearchSuggestionItem>(page.Count);
            foreach (var result in page)
            {
                if (string.IsNullOrEmpty(result.RoleIdSimple)) continue;
                if (!_seenRoleIds.Add(result.RoleIdSimple)) continue;
                items.Add(ToItem(result));
            }
            return items;
        }

        private void AppendDedup(List<UnifiedSearchResult> page)
        {
            if (page.Count == 0) return;

            var merged = new List<PlayerSearchSuggestionItem>(Items.Count + page.Count);
            merged.AddRange(Items);
            foreach (var result in page)
            {
                if (string.IsNullOrEmpty(result.RoleIdSimple)) continue;
                if (!_seenRoleIds.Add(result.RoleIdSimple)) continue;
                merged.Add(ToItem(result));
            }
            Items.ReplaceAll(merged);
        }

        private static PlayerSearchSuggestionItem ToItem(UnifiedSearchResult result) => new()
        {
            RoleId = result.RoleIdSimple,
            Server = result.Server,
            DisplayName = result.RoleName ?? string.Empty,
            AvatarUrl = result.Avatar ?? string.Empty,
            RankIconUrl = result.LevelImg ?? string.Empty,
            RankName = result.LevelName ?? string.Empty,
            MetaText = BuildMetaText(result),
        };

        /// <summary>
        /// 第二行文案：UID 打头，随后是接口给出的其余字段（有表头列名就带上列名）。
        /// 段位不在这里——它单独占一列摆在行右侧。
        /// 除 UID 外的字段都收在 <see cref="UnifiedSearchResult.Fields"/> 里，这里全量拼出来，
        /// 接口以后多给一列也能直接显示，不需要改这里。
        /// </summary>
        private static string BuildMetaText(UnifiedSearchResult result)
        {
            var parts = new List<string>(result.Fields.Count + 1);
            if (!string.IsNullOrEmpty(result.RoleIdSimple)) parts.Add("UID " + result.RoleIdSimple);

            foreach (var field in result.Fields)
            {
                var text = field.Text?.Trim() ?? string.Empty;
                if (text.Length == 0) continue;
                var title = field.Title?.Trim() ?? string.Empty;
                parts.Add(title.Length == 0 ? text : title + " " + text);
            }

            return string.Join(" · ", parts);
        }
    }
}
