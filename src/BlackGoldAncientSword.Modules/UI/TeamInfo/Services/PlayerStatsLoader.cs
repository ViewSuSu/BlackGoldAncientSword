using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{

    [Component(ComponentLifetime.Singleton)]
    public class PlayerStatsLoader
    {
        private readonly ILocalizedTextProvider _localizedText;
        private readonly HeyboxHomeDataProvider _homeData;

        public PlayerStatsLoader(ILocalizedTextProvider localizedText, HeyboxHomeDataProvider homeData)
        {
            _localizedText = localizedText;
            _homeData = homeData;
        }

        public async Task<PlayerStatsLoadResult?> LoadAsync(
            PlayerSourceContext ctx,
            string? seasonKey,
            GameMode gameMode,
            CancellationToken ct)
        {
            var battleTid = gameMode.ToHeyBoxBattleTid().ToString(CultureInfo.InvariantCulture);
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, seasonKey, battleTid, ct).ConfigureAwait(false);
            var stats = UnifiedMapper.MapSeasonSummary(home?.Result);

            if (stats?.Stats == null) return null;

            var result = new PlayerStatsLoadResult();
            foreach (var stat in stats.Stats)
            {
                if (string.IsNullOrEmpty(stat.Key)) continue;

                // key 与 value 都用后端原文：desc 直接做行标题，value 原样透传，
                // 前端不做任何换算、兜底、写死取值。Stats 与 Metrics 的 key 同源：
                // UpdateDiffs 按 metric.Key 查各成员 Stats 字典，成员间 key 空间一致（同一接口）。
                result.Stats[stat.Key] = stat.Value ?? string.Empty;

                result.Metrics.Add(new PlayerStatMetric(
                    stat.Key,
                    string.IsNullOrEmpty(stat.Name) ? stat.Key : stat.Name));
            }

            if (stats.Grade != null)
            {
                result.RankName = stats.Grade.GradeName;
                result.RankIcon = stats.Grade.GradeIcon;
                result.RankScore = stats.Grade.GradeScore;
                var gm = (int)gameMode;
                result.PageStarCount = GetStarCount(result.RankScore, gm);
                result.PageHasStars = ((GameMode)gm).IsRankMode() && result.RankScore >= 4500;
                var gradeName = stats.Grade.GradeName == null ? string.Empty : stats.Grade.GradeName.Trim();
                if (gradeName.Length > 0)
                {
                    result.PageRankName = gradeName;
                }
                else
                {
                    var pageRankBase = GetRankNameForScore(result.RankScore, gm);
                    result.PageRankName = result.PageHasStars
                        ? pageRankBase
                        : pageRankBase + GetSubTierName(result.RankScore, gm);
                }
            }

            result.WaitUpdate = stats.WaitUpdate;

            return result;
        }

        public async Task<(List<UnifiedSeason> Seasons, string? CurrentSeasonKey)> FetchSeasonsAsync(
            PlayerSourceContext ctx, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, season: null, battleTid: null, ct).ConfigureAwait(false);
            return (UnifiedMapper.MapSeasons(home?.Result?.Seasons), home?.Result?.Season);
        }

        public string GetRankNameForScore(double score, int gameMode = 0)
        {
            if (((GameMode)gameMode).IsRankMode())
            {
                if (score >= 7500) return _localizedText.Get("Rank.WuLiangFanTian", "无量梵天");
                if (score >= 6000) return _localizedText.Get("Rank.WuXiangLongWang", "无相龙王");
                if (score >= 5000) return _localizedText.Get("Rank.WuShuangXiuLuo", "无双修罗");
                if (score >= 4500) return _localizedText.Get("Rank.WuJianXiuLuo", "无间修罗");
                if (score >= 4000) return _localizedText.Get("Rank.ZhuiRi", "坠日");
                if (score >= 3500) return _localizedText.Get("Rank.ShiYue", "蚀月");
                if (score >= 3000) return _localizedText.Get("Rank.YunXing", "陨星");
                if (score >= 2500) return _localizedText.Get("Rank.BoJin", "铂金");
                if (score >= 2000) return _localizedText.Get("Rank.HuangJin", "黄金");
                if (score >= 1500) return _localizedText.Get("Rank.BaiYin", "白银");
                if (score >= 1000) return _localizedText.Get("Rank.QingTong", "青铜");
                return string.Empty;
            }
            else
            {
                if (score >= 7000) return _localizedText.Get("Rank.WuJianTaiDou", "无间泰斗");
                if (score >= 6500) return _localizedText.Get("Rank.YuTianZunZhe", "御天尊者");
                if (score >= 6000) return _localizedText.Get("Rank.JieXuShengZhu", "劫虚圣主");
                if (score >= 5500) return _localizedText.Get("Rank.QiongCangKuiShou", "穹苍魁首");
                if (score >= 5000) return _localizedText.Get("Rank.RiYaoMingSu", "日曜名宿");
                if (score >= 4500) return _localizedText.Get("Rank.XingYueZongShi", "星月宗师");
                if (score >= 4000) return _localizedText.Get("Rank.YunXiaoWuSheng", "云霄武圣");
                if (score >= 3500) return _localizedText.Get("Rank.JueDingGaoShou", "绝顶高手");
                if (score >= 3000) return _localizedText.Get("Rank.FanChenWuShi", "凡尘武师");
                return _localizedText.Get("Rank.FanChenWuShi", "凡尘武师");
            }
        }

        public static int GetStarCount(double score, int gameMode = 0)
        {
            if (!((GameMode)gameMode).IsRankMode()) return 0;
            if (score >= 4500) return (int)((score - 4500) / 100);
            int[] thresholds = { 4500, 4000, 3500, 3000, 2500, 2000, 1500, 1000 };
            for (int t = 0; t < thresholds.Length - 1; t++)
            {
                if (score >= thresholds[t + 1])
                {
                    var remaining = thresholds[t] - score;
                    return (int)((remaining + 99) / 100);
                }
            }
            return 0;
        }

        private static string GetSubTierName(double score, int gameMode)
        {
            if (!((GameMode)gameMode).IsRankMode()) return string.Empty;
            if (score < 1000 || score >= 4500) return string.Empty;

            var tierBase = GetTierBaseForSubTier(score);
            var offset = score - tierBase;
            var subTierIndex = (int)(offset / 100);
            var names = new[] { "5", "4", "3", "2", "1" };
            if (subTierIndex >= 0 && subTierIndex < names.Length)
                return names[subTierIndex];
            return string.Empty;
        }

        private static double GetTierBaseForSubTier(double score)
        {
            if (score >= 4500) return 4500;
            if (score >= 4000) return 4000;
            if (score >= 3500) return 3500;
            if (score >= 3000) return 3000;
            if (score >= 2500) return 2500;
            if (score >= 2000) return 2000;
            if (score >= 1500) return 1500;
            if (score >= 1000) return 1000;
            return 0;
        }

    }

    public class PlayerStatsLoadResult
    {
        public Dictionary<string, string> Stats { get; } = new();

        public List<PlayerStatMetric> Metrics { get; } = new();

        public string RankName { get; set; } = string.Empty;
        public string RankIcon { get; set; } = string.Empty;
        public double RankScore { get; set; }
        public string PageRankName { get; set; } = string.Empty;
        public int PageStarCount { get; set; }
        public bool PageHasStars { get; set; }
        public bool WaitUpdate { get; set; }
    }

    public readonly record struct PlayerStatMetric(string Key, string Label);
}
