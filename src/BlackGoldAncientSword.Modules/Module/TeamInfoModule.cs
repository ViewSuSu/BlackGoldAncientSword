using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Views;

namespace BlackGoldAncientSword.Modules.Module
{
    [Module(OnDemand = false)]
    public class TeamInfoModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
            // Eager 解析：冷启动若游戏已处于英雄选择阶段（用户进入英雄选择后才打开程序的场景），
            // GameLogMonitor.StartAsync 会在 replay 完毕后 PublishSnapshot 触发 BattleJoined。
            // 该事件需要 TeamInfoPageViewModel 已订阅 GameStatusRecognized 才能启动 OCR loop。
            // 而该 VM 默认 lazy 实例化（首次导航到 TeamInfoPage 时才 ctor），若用户未切换到该页则永不订阅。
            // 这里在模块 Init 阶段（早于 MainWindowVM ctor 的 fire-and-forget StartAsync）主动 resolve 一次，
            // 保证 ctor 中的订阅在快照事件到达前完成。
            containerProvider.Resolve<TeamInfoPageViewModel>();
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // [Component] 已经被 ModuleCatalogConfigManager.RegisterModuleLayer() 自动扫描注册为 Singleton；
            // 这里再显式注册一次，让模块依赖图在 RegisterTypes 中一目了然，便于排查与文档。
            containerRegistry.RegisterSingleton<TeamOcrCoordinator>();
            containerRegistry.RegisterSingleton<TeamMemberLoader>();
            containerRegistry.RegisterSingleton<PlayerStatsLoader>();
            containerRegistry.RegisterSingleton<TeamInfoPageViewModel>();
            containerRegistry.RegisterForNavigation<TeamInfoPage, TeamInfoPageViewModel>();
        }
    }
}
