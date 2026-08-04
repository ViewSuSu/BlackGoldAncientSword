using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// MainContentRegion 的统一导航入口。本类拥有该 Region 的 NavigationJournal 所有权：
    /// 用自己维护的有界 <see cref="_history"/> 提供回退能力，并在每次导航完成的回调中清空
    /// Prism Journal，防止 NavigationParameters / View 实例被 Journal 长期持有造成内存泄漏。
    ///
    /// 其他模块禁止直接调用 region.NavigationService.Journal 上的 GoBack / GoForward，
    /// 该 Journal 会被本类周期性清空，结果不可预期。仓库已通过 grep 验证当前无外部调用点。
    ///
    /// 视图键约定：本实现依赖"Prism 注册键 == View 类型名"的项目约定（见 <c>PageNames</c>），
    /// <see cref="GetActiveContentName"/> 直接取 ActiveView 的类型名作为 viewName 反向匹配。
    ///
    /// 线程契约（硬约束）：本类对 _history / Prism Journal 的访问不加锁，所有公共方法
    /// 与 Prism 的 RequestNavigate 回调必须发生在 UI 线程上。当前 Prism RegionNavigationService
    /// 在 WPF 上是完全同步路径（OnCompleted 在 RequestNavigate 内同步触发），不存在
    /// in-flight 回调；接口注释中的"重入约束"正是此同步特性的直接后果。若未来引入异步
    /// IConfirmNavigationRequest，必须重新设计本类的线程模型（Dispatcher marshaling + 状态机），
    /// 而非简单加内存屏障。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class MainContentNavigator : IMainContentNavigationService
    {
        public event Action<string>? Navigated;

        private readonly IRegionManager _regionManager;
        private readonly IModuleManager _moduleManager;

        // 50 层覆盖典型用户的页面路径深度；单条字符串通常 < 100 字节，总占用约 < 5 KB，
        // 远低于 Prism Journal 长期持有 View / NavigationParameters 的内存代价。
        private const int MaxHistoryDepth = 50;

        private readonly BoundedStack<string> _history = new(MaxHistoryDepth);

        // 单 UI 线程契约下用作纯逻辑取消令牌：Remove 时 ++，使此前所有未完成的旧导航回调直接 return。
        private int _epoch;

        public bool CanGoBack => _history.Count > 0;

        public MainContentNavigator(IRegionManager regionManager, IModuleManager moduleManager)
        {
            _regionManager = regionManager;
            _moduleManager = moduleManager;
        }

        public void NavigateTo(string viewName, NavigationParameters? navigationParameters = null)
        {
            var current = GetActiveContentName();

            // Skip if already on the target page
            if (current == viewName)
                return;

            EnsureModuleLoaded(viewName);

            // current 在本方法内不再赋值，闭包直接捕获即可；仅当导航真正成功时才把它压入回退栈。
            Action? onSuccess = string.IsNullOrEmpty(current)
                ? null
                : () => _history.Push(current);

            // 捕获当前 epoch 作为本次导航的"代号"。
            // 真正的 ++_epoch 在 Remove() 内：之后这里捕获到的旧 epoch != _epoch，回调会直接 return，
            // 等价于"此前所有尚未完成的导航回调被作废"。
            int capturedEpoch = _epoch;

            void OnCompleted(NavigationResult result)
            {
                if (capturedEpoch != _epoch)
                    return;
                HandleNavigationCompleted(result, onSuccess, raiseNavigatedAs: viewName);
            }

            try
            {
                if (navigationParameters != null)
                {
                    _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, viewName, OnCompleted, navigationParameters);
                }
                else
                {
                    _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, viewName, OnCompleted);
                }
            }
            catch (Exception ex)
            {
                // KeyNotFoundException / UpdateRegionsException / 其他异常处理逻辑相同：
                // 记录原因并触发 Navigated（参数为真实激活页），让 UI 绑定刷新到真实状态。
                AppLog.Error(ex, nameof(MainContentNavigator), "RequestNavigate 失败");
                Navigated?.Invoke(GetActiveContentName());
            }
        }

        public void GoBack()
        {
            // Peek 而非 Pop：导航失败时回退栈保持原样，避免"点了回退、页面没换、历史还少一层"。
            // 真正的 Pop 延迟到 HandleNavigationCompleted 的成功分支，与 NavigateTo 的"仅成功时 Push"对称。
            string target;
            Action? onSuccess;
            if (_history.Count > 0)
            {
                target = _history.Peek();
                onSuccess = () =>
                {
                    // 单 UI 线程下栈顶不会被其它路径偷改，但保留比对让"仅成功时 Pop"的意图显式：
                    // 万一未来某条新路径在导航完成前改动了 _history，至少不会盲目丢掉一条不相关的历史。
                    if (_history.Count > 0 && string.Equals(_history.Peek(), target, StringComparison.Ordinal))
                    {
                        _history.Pop();
                    }
                };
            }
            else
            {
                target = PageNames.StatsPage;
                onSuccess = null;
            }

            EnsureModuleLoaded(target);

            // 与 NavigateTo 对称：捕获 epoch，Remove 介入后让 in-flight 的 GoBack 回调直接 return，
            // 避免对已清空的 _history 做 Pop / 触发错误的 Navigated 通知。
            int capturedEpoch = _epoch;

            void OnCompleted(NavigationResult result)
            {
                if (capturedEpoch != _epoch)
                    return;
                HandleNavigationCompleted(result, onSuccess, raiseNavigatedAs: target);
            }

            _regionManager.RequestNavigate(GlobalConstant.MainContentRegion, target, OnCompleted);
        }

        public void Remove()
        {
            var region = _regionManager.Regions[GlobalConstant.MainContentRegion];
            if (!region.Views.Any())
                return;

            // 先清 Journal、再清 _history、最后 RemoveAll；
            // 避免依赖"Prism RemoveAll 之后 NavigationService 仍可用"这一隐式契约。
            ClearPrismJournal();
            _history.Clear();
            // 增加 epoch 让任何尚未完成的旧导航回调直接 return
            _epoch++;
            region.RemoveAll();
            Navigated?.Invoke(string.Empty);
        }

        /// <summary>
        /// 导航完成统一处理。仅当导航真正成功（<c>result.Result == true</c>）时才执行
        /// 调用方提供的 <paramref name="onSuccess"/> 副作用（更新回退栈）。
        /// 失败时不更新回退栈，但仍触发 <see cref="Navigated"/>（参数为实际激活的 View 名），
        /// 让 UI 绑定（CanGoBack / 当前页指示）刷新到真实状态——避免"按了回退没反应、UI 也无反馈"
        /// 的死循环：订阅方可对比 raiseNavigatedAs 与回调里收到的 viewName 识别失败。
        ///
        /// Journal 清空在任一分支都执行：Prism 在导航失败时通常不会向 Journal Push 新 entry，
        /// Clear 退化为 no-op；即便某些路径下 Push 了，主动清空也不会破坏本类自维护的回退能力。
        /// </summary>
        private void HandleNavigationCompleted(NavigationResult result, Action? onSuccess, string raiseNavigatedAs)
        {
            ClearPrismJournal();

            if (result.Result == true)
            {
                onSuccess?.Invoke();
                Navigated?.Invoke(raiseNavigatedAs);
                return;
            }

            if (result.Error != null)
            {
                Debug.WriteLine($"[{nameof(MainContentNavigator)}] 导航失败：{result.Error.Message}");
            }
            else
            {
                // result.Result 为 false 或 null 且无 Error，常见于 IConfirmNavigationRequest 拒绝、
                // 或 Prism 在某些边界条件下既未成功也未抛错。记录便于诊断"为什么页面没切换"。
                Debug.WriteLine($"[{nameof(MainContentNavigator)}] 导航到 '{raiseNavigatedAs}' 未完成：result.Result={result.Result?.ToString() ?? "null"}");
            }

            // 失败也通知一次：参数为实际激活的 View 名（通常仍是原页），
            // 订阅方据此对比"自己请求的 raiseNavigatedAs" 识别失败、刷新 CanGoBack 等绑定。
            Navigated?.Invoke(GetActiveContentName());
        }

        /// <summary>
        /// 清空 Prism 区域导航日志栈，避免 RegionNavigationJournalEntry / NavigationParameters
        /// 随每次导航无限增长。本类已用 _history 维护回退栈，Prism Journal 在此场景下是冗余的。
        /// </summary>
        private void ClearPrismJournal()
        {
            if (!_regionManager.Regions.ContainsRegionWithName(GlobalConstant.MainContentRegion))
                return;
            _regionManager.Regions[GlobalConstant.MainContentRegion].NavigationService?.Journal?.Clear();
        }

        private string GetActiveContentName()
        {
            var region = _regionManager.Regions[GlobalConstant.MainContentRegion];
            var activeView = region.ActiveViews.FirstOrDefault();
            return activeView?.GetType().Name ?? string.Empty;
        }

        private void EnsureModuleLoaded(string viewName)
        {
            if (!viewName.EndsWith("Page"))
                return;

            var moduleName = viewName.Replace("Page", "Module");
            try
            {
                _moduleManager.LoadModule(moduleName);
            }
            catch (Exception ex)
            {
                // Module 可能已加载或未注册为 OnDemand；记录原因以便诊断真正的注册缺失。
                AppLog.Error(ex, nameof(MainContentNavigator), $"LoadModule({moduleName}) 失败");
            }
        }

        /// <summary>
        /// 容量受限的栈：超过容量时从底部丢弃最老元素，保留最近 N 个。
        /// 仅供本类内部使用，对外不暴露。
        /// </summary>
        private sealed class BoundedStack<T>
        {
            private readonly LinkedList<T> _list = new();
            public int Capacity { get; }
            public int Count => _list.Count;

            public BoundedStack(int capacity)
            {
                Capacity = capacity;
            }

            public void Push(T value)
            {
                _list.AddLast(value);
                while (_list.Count > Capacity)
                {
                    _list.RemoveFirst();
                }
            }

            public T Pop()
            {
                var last = _list.Last
                    ?? throw new InvalidOperationException($"BoundedStack.{nameof(Pop)} 在空栈上调用，调用方需先用 Count > 0 守护。");
                var value = last.Value;
                _list.RemoveLast();
                return value;
            }

            public T Peek()
            {
                var last = _list.Last
                    ?? throw new InvalidOperationException($"BoundedStack.{nameof(Peek)} 在空栈上调用，调用方需先用 Count > 0 守护。");
                return last.Value;
            }

            public void Clear() => _list.Clear();
        }
    }
}
