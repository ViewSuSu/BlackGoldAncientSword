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
                var val = string.IsNullOrEmpty(stat.Value) ? "-" : stat.Value;

                var normalizedKey = NormalizeStatKey(stat.Key);

                var displayVal = normalizedKey.Contains("live_time", System.StringComparison.OrdinalIgnoreCase)
                    ? FormatSurvivalTime(val)
                    : val;
                result.Stats[normalizedKey] = displayVal;

                result.Metrics.Add(new PlayerStatMetric(
                    normalizedKey,
                    string.IsNullOrEmpty(stat.Name) ? stat.Key : stat.Name));

                switch (normalizedKey)
                {
                    case "avg_kill": result.AvgKill = val; break;
                    case "top5_rate": result.Top5Rate = val; break;
                    case "avg_damage": result.AvgDamage = val; break;
                    case "avg_total_live_time": result.SurviveTime = FormatSurvivalTime(val); break;
                }
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

            return result;
        }

        private static string NormalizeStatKey(string key)
        {
            return key switch
            {
                "总场次" => "round",
                "夺冠" => "win",
                "夺冠率" => "win_rate",
                "前五" => "top5",
                "前五率" => "top5_rate",
                "场均伤害" => "avg_damage",
                "场均击杀" => "avg_kill",
                "KD" => "kd",
                "场均恢复" => "avg_cure",
                "最高伤害" => "max_damage",
                "最高恢复" => "max_cure",
                "最高击杀" => "max_kill",
                "场均存活时间" => "avg_total_live_time",
                "伤害/击杀" => "dmg_per_kill",
                "总对局时间" => "total_time",
                "场均振刀" => "avg_shock",
                _ => key,
            };
        }

        public async Task<(List<UnifiedSeason> Seasons, string? CurrentSeasonKey)> FetchSeasonsAsync(
            PlayerSourceContext ctx, CancellationToken ct)
        {
            var home = await _homeData.GetAsync(ctx.RoleId, ctx.Server, season: null, battleTid: null, ct).ConfigureAwait(false);
            return (UnifiedMapper.MapSeasons(home?.Result?.Seasons), home?.Result?.Season);
        }

        public static string FormatSurvivalTime(string secondsStr)
        {
            if (double.TryParse(secondsStr, out double seconds))
            {
                var minutes = (int)(seconds / 60);
                var remainSeconds = (int)(seconds % 60);
                return $"{minutes}分{remainSeconds:D2}秒";
            }
            return secondsStr;
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

        public string? AvgKill { get; set; }
        public string? Top5Rate { get; set; }
        public string? AvgDamage { get; set; }
        public string? SurviveTime { get; set; }
        public string RankName { get; set; } = string.Empty;
        public string RankIcon { get; set; } = string.Empty;
        public double RankScore { get; set; }
        public string PageRankName { get; set; } = string.Empty;
        public int PageStarCount { get; set; }
        public bool PageHasStars { get; set; }
    }

    public readonly record struct PlayerStatMetric(string Key, string Label);
}
