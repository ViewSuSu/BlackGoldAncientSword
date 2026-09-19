using System.Collections.Generic;
using System.Linq;
using BlackGoldAncientSword.Framework.UI.Controls;
using BlackGoldAncientSword.Tests.Settings;
using Xunit;

namespace BlackGoldAncientSword.Tests.UI.Controls
{
    /// <summary>
    /// 右下角队伍弹窗成员列表的刷新契约：成员数量与顺序恒等于这一次下发的名单，
    /// 且稳定名单下复用已有成员对象（WPF 可视化树与 BitmapImage 复用依赖这一点）。
    /// <para>
    /// 回归场景：成员昵称不保证已有（可能始终为空）。旧实现按 UserName 匹配已有成员，
    /// 空名字的成员既匹配不上（每次刷新都被当新成员插入）、又不会被移除，三排队弹窗会堆出
    /// 5 个以上的空位。
    /// </para>
    /// </summary>
    [Collection(nameof(PrismTestCollection))]
    public class TeamOverlayViewModelTests
    {
        private static TeamOverlayMemberItem Member(string name) => new() { UserName = name };

        /// <summary>三排：两名队友昵称尚未查回（空），本地用户居中且有名字。</summary>
        private static List<TeamOverlayMemberItem> TrioWithUnresolvedTeammates() => new()
        {
            Member(string.Empty),
            Member("爱的供养丶"),
            Member(string.Empty)
        };

        [Fact]
        public void UpdateMembers_RepeatedRefreshWithUnresolvedNames_KeepsMemberCountStable()
        {
            var vm = new TeamOverlayViewModel();

            // 每张卡加载完都会刷新一次弹窗，一次识别里会刷新多轮。
            for (int i = 0; i < 5; i++)
                vm.UpdateMembers(TrioWithUnresolvedTeammates());

            Assert.Equal(3, vm.Members.Count);
            Assert.Equal("爱的供养丶", vm.Members[1].UserName);
        }

        [Fact]
        public void UpdateMembers_NamesArrivingLater_UpdatesInPlaceWithoutChangingCount()
        {
            var vm = new TeamOverlayViewModel();
            vm.UpdateMembers(TrioWithUnresolvedTeammates());
            var before = vm.Members.ToList();

            vm.UpdateMembers(new List<TeamOverlayMemberItem>
            {
                Member("队友甲"),
                Member("爱的供养丶"),
                Member("队友乙")
            });

            Assert.Equal(3, vm.Members.Count);
            Assert.Same(before[0], vm.Members[0]);
            Assert.Same(before[1], vm.Members[1]);
            Assert.Same(before[2], vm.Members[2]);
            Assert.Equal("队友甲", vm.Members[0].UserName);
            Assert.Equal("队友乙", vm.Members[2].UserName);
        }

        /// <summary>
        /// 回归场景：后台补齐（间隔请求）在弹窗已经显示之后才拿回真名。
        /// 此时必须能把名字刷进弹窗——屏幕上的成员格显示名要从空变成真名。
        /// </summary>
        [Fact]
        public void UpdateMembers_NameArrivingAfterShow_RefreshesVisibleName()
        {
            var vm = new TeamOverlayViewModel();
            // 首次下发：队友昵称都还没查回来，弹窗已显示但队友格是空的。
            vm.UpdateMembers(TrioWithUnresolvedTeammates());
            Assert.Equal(string.Empty, vm.Members[0].UserName);
            Assert.Equal(string.Empty, vm.Members[2].UserName);

            // 间隔补齐第 1 轮：只拿回左侧队友的名字。
            vm.UpdateMembers(new List<TeamOverlayMemberItem>
            {
                Member("队友甲"),
                Member("爱的供养丶"),
                Member(string.Empty)
            });

            Assert.Equal("队友甲", vm.Members[0].UserName);
            Assert.Equal(string.Empty, vm.Members[2].UserName);

            // 间隔补齐第 2 轮：右侧队友的名字也回来了。
            vm.UpdateMembers(new List<TeamOverlayMemberItem>
            {
                Member("队友甲"),
                Member("爱的供养丶"),
                Member("队友乙")
            });

            Assert.Equal("队友甲", vm.Members[0].UserName);
            Assert.Equal("队友乙", vm.Members[2].UserName);
            Assert.Equal(3, vm.Members.Count);
        }

        [Fact]
        public void UpdateMembers_ShrinkingRoster_RemovesExtraMembers()
        {
            var vm = new TeamOverlayViewModel();
            vm.UpdateMembers(TrioWithUnresolvedTeammates());

            // 队友退出：三排变双排，名单只剩 2 项。
            vm.UpdateMembers(new List<TeamOverlayMemberItem> { Member(string.Empty), Member("爱的供养丶") });

            Assert.Equal(2, vm.Members.Count);
            Assert.Equal("爱的供养丶", vm.Members[1].UserName);
        }

        [Fact]
        public void UpdateMembers_EmptyList_ClearsMembers()
        {
            var vm = new TeamOverlayViewModel();
            vm.UpdateMembers(TrioWithUnresolvedTeammates());

            vm.UpdateMembers(new List<TeamOverlayMemberItem>());

            Assert.Empty(vm.Members);
            Assert.False(vm.HasMembers);
        }
    }
}
