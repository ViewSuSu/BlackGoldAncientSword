using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.UI.Controls;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;
using BlackGoldAncientSword.Tests.Settings;
using Prism.Regions;

namespace BlackGoldAncientSword.Tests.UI.TeamInfo
{
    /// <summary>
    /// 队伍卡「详情」跳转战绩页时携带的参数契约：**每张卡都必须带上目标玩家名**。
    /// <para>
    /// 回归：本地用户卡原先刻意不带名字，而战绩页把"没带任何目标信息"理解成「切页回来 /
    /// 底部导航」，那条路径会保持当前浏览对象不动 —— 于是先点队友详情、再点本地卡详情时，
    /// 战绩页仍停在队友的数据上。
    /// </para>
    /// </summary>
    [Collection(nameof(PrismTestCollection))]
    public class TeamInfoNavigationTests
    {
        // ===== 捕获导航参数 =====
        private sealed class CapturingNavigationService : IMainContentNavigationService
        {
            public string? LastViewName { get; private set; }
            public NavigationParameters? LastParameters { get; private set; }

            public bool CanGoBack => false;

#pragma warning disable CS0067
            public event Action<string>? Navigated;
#pragma warning restore CS0067

            public void NavigateTo(string viewName, NavigationParameters? navigationParameters = null)
            {
                LastViewName = viewName;
                LastParameters = navigationParameters;
            }

            public void GoBack() { }

            public void Remove() { }
        }

        private sealed class FakePlayerPrefsService : IPlayerPrefsService
        {
            public PlayerPrefsData Current { get; } = new()
            {
                PlayerId = "l77c000015949400120163",
                PlayerName = "爱的供养丶",
                OriginalPlayerName = "爱的供养丶",
                IsLoaded = true,
            };

            public Task LoadAsync() => Task.CompletedTask;
        }

        private sealed class ImmediateDispatcher : IUIDispatcher
        {
            public bool CheckAccess() => true;
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
            public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
            public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
            public void BeginInvoke(Action action) => action();
        }

        private sealed class NoopGameStatusMonitor : IGameStatusMonitor
        {
            public GameStatus CurrentStatus => GameStatus.Unknown;
            public bool IsRunning => false;

#pragma warning disable CS0067
            public event EventHandler<GameStatusChangedEventArgs>? GameStatusRecognized;
#pragma warning restore CS0067

            public void Start() { }
            public void Stop() { }
            public void NotifyStatus(GameStatus status) { }
            public void Dispose() { }
        }

        private sealed class NoopTeammateMonitor : ICcMiniTeammateMonitor
        {
            public IReadOnlyList<string> TeammateUids => Array.Empty<string>();
            public bool HasRecognized => false;

#pragma warning disable CS0067
            public event EventHandler<CcMiniTeammatesEventArgs>? TeammatesReady;
#pragma warning restore CS0067

            public void Start() { }
            public void Stop() { }
            public void Reset(DateTime? matchStartTime = null) { }
            public void Dispose() { }
        }

        private sealed class NoopTeamOverlayService : ITeamOverlayService
        {
#pragma warning disable CS0067
            public event Action? Dismissed;
            public event Action? NavigateToTeamInfoRequested;
#pragma warning restore CS0067

            public void Show(IList<TeamOverlayMemberItem> members) { }
            public void Hide() { }
        }

        private sealed class NoopClipboardService : IClipboardService
        {
            public bool TrySetText(string text) => true;
        }

        private sealed class NoopTipMessageService : ITipMessageService
        {
            public void Show(string message, TipMessageType type = TipMessageType.Info) { }
            public void ShowError(string message) { }
            public void ShowInfo(string message) { }
        }

        private sealed class PassthroughLocalizedText : ILocalizedTextProvider
        {
            public string Get(string key, string fallback) => fallback;
        }

        private static (TeamInfoPageViewModel Vm, CapturingNavigationService Navigation, TeamMemberInfo Local, TeamMemberInfo Teammate) Build()
        {
            var navigation = new CapturingNavigationService();
            var cache = new HeyboxRequestCache();
            var homeData = new HeyboxHomeDataProvider(cache);
            var clipboard = new NoopClipboardService();
            var text = new PassthroughLocalizedText();
            var tip = new NoopTipMessageService();
            var memberLoader = new TeamMemberLoader(new PlayerStatsLoader(text, homeData), homeData, cache);

            var vm = new TeamInfoPageViewModel(
                new NoopGameStatusMonitor(),
                new FakePlayerPrefsService(),
                new NoopTeamOverlayService(),
                navigation,
                new ImmediateDispatcher(),
                clipboard,
                new NoopTeammateMonitor(),
                memberLoader,
                new HeyboxPlayerRefresher(),
                text,
                tip);

            var teammate = new TeamMemberInfo(clipboard, text, tip)
            {
                UserName = "uipe000001677200140163",
                DisplayName = "总有一人迷了路",
                IsLocalUser = false,
            };
            var local = new TeamMemberInfo(clipboard, text, tip)
            {
                UserName = "爱的供养丶",
                DisplayName = string.Empty,
                IsLocalUser = true,
            };
            return (vm, navigation, local, teammate);
        }

        private static void InvokeNavigateToStats(TeamInfoPageViewModel vm, TeamMemberInfo member)
        {
            var method = typeof(TeamInfoPageViewModel).GetMethod(
                "NavigateToMemberStats", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(vm, new object[] { member });
        }

        private static string? CarriedTargetName(CapturingNavigationService navigation)
            => navigation.LastParameters?.GetValue<string>(NavigationParameterKeys.TargetPlayerName);

        [Fact]
        public void FromLocalCard_Should_Carry_Local_Player_Name()
        {
            var (vm, navigation, local, _) = Build();

            InvokeNavigateToStats(vm, local);

            Assert.Equal(PageNames.StatsPage, navigation.LastViewName);
            Assert.Equal("爱的供养丶", CarriedTargetName(navigation));
        }

        [Fact]
        public void FromTeammateCard_Should_Carry_Real_Name()
        {
            var (vm, navigation, _, teammate) = Build();

            InvokeNavigateToStats(vm, teammate);

            Assert.Equal("总有一人迷了路", CarriedTargetName(navigation));
        }

        [Fact]
        public void Should_Fall_Back_To_Uid_When_Teammate_Has_No_Real_Name()
        {
            var (vm, navigation, _, teammate) = Build();
            teammate.DisplayName = string.Empty;

            InvokeNavigateToStats(vm, teammate);

            Assert.Equal("uipe000001677200140163", CarriedTargetName(navigation));
        }
    }
}
