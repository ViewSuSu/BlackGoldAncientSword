using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// Debug 测试页的静态 mock 队员数据（仅左右卡使用；中间卡恒为本地用户真实查询）。
    /// 用于在无需进入英雄选择阶段的情况下，验证 TeamMemberCard 渲染与 diff 对比效果。
    /// </summary>
    public static class MockTeamData
    {
        /// <summary>
        /// 构造一张 mock 队员卡。段位图标（RankIcon）留空（测试页视觉聚焦在统计/diff）。
        /// </summary>
        public static TeamMemberInfo CreateLeftMember(
            IClipboardService clipboard, ILocalizedTextProvider localizedText, ITipMessageService tipMessage)
        {
            var m = new TeamMemberInfo(clipboard, localizedText, tipMessage)
            {
                UserName = "千面_御剑",
                DisplayName = "千面_御剑",
                UID = "100233445",
                Level = "Lv.120",
                AvatarUrl = string.Empty,
                PageRankName = "蚀月2",
                PageStarCount = 0,
                PageHasStars = false,
                RankScore = 3488,
                RankTierScore = 88,
            };
            FillStats(m, round: 120, win: 38, avgKill: 4.2, top5Rate: 32.5, avgDamage: 12560, surviveMin: 18, avgCure: 3200, kd: 4.8);
            return m;
        }

        /// <summary>
        /// 构造另一张 mock 队员卡（数值与左卡明显不同，便于观察 diff 对比）。
        /// </summary>
        public static TeamMemberInfo CreateRightMember(
            IClipboardService clipboard, ILocalizedTextProvider localizedText, ITipMessageService tipMessage)
        {
            var m = new TeamMemberInfo(clipboard, localizedText, tipMessage)
            {
                UserName = "八荒·一粟",
                DisplayName = "八荒·一粟",
                UID = "100876512",
                Level = "Lv.96",
                AvatarUrl = string.Empty,
                PageRankName = "铂金5",
                PageStarCount = 0,
                PageHasStars = false,
                RankScore = 2550,
                RankTierScore = 50,
            };
            FillStats(m, round: 88, win: 21, avgKill: 2.9, top5Rate: 24.0, avgDamage: 9850, surviveMin: 14, avgCure: 2100, kd: 3.1);
            return m;
        }

        /// <summary>
        /// 用与真实后端一致的英文 metric key 填充 mock 数据，
        /// 保证统计行模板（本地用户 Metrics）能命中取到值。
        /// </summary>
        private static void FillStats(
            TeamMemberInfo m, int round, int win, double avgKill, double top5Rate,
            double avgDamage, int surviveMin, double avgCure, double kd)
        {
            m.Stats["round"] = round.ToString();
            m.Stats["win"] = win.ToString();
            m.Stats["win_rate"] = $"{win * 100.0 / round:F1}%";
            m.Stats["avg_kill"] = avgKill.ToString("F1");
            m.Stats["top5_rate"] = $"{top5Rate:F1}%";
            m.Stats["avg_damage"] = avgDamage.ToString("F0");
            m.Stats["avg_cure"] = avgCure.ToString("F0");
            m.Stats["kd"] = kd.ToString("F1");
            m.Stats["avg_total_live_time"] = $"{surviveMin}分{0:D2}秒";
        }
    }
}
