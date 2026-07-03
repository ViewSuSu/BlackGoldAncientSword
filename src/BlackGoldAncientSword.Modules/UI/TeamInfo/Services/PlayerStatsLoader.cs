using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// 拉取单个玩家在指定赛季/游戏模式下的 stats，并把响应解析成 ViewModel 友好的 DTO。
    /// 与 Stats 模块的同名 Loader 完全独立：此 Loader 仅服务于 TeamInfo 页面的成员对比。
    /// SearchRecord 后由 VM 构造 <see cref="PlayerSourceContext"/> 分派 miniProgram / heyBox。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class PlayerStatsLoader
    {
        private readonly ILocalizedTextProvider _localizedText;

        public PlayerStatsLoader(ILocalizedTextProvider localizedText)
        {
            _localizedText = localizedText;
        }

        /// <summary>
        /// 拉取 stats。任何异常向上抛出由调用方处理；返回 null 表示 API 未返回有效数据。
        /// </summary>
        public async Task<PlayerStatsLoadResult?> LoadAsync(
            PlayerSourceContext ctx,
            double? seasonId,
            GameMode gameMode,
            CancellationToken ct)
        {
            UnifiedPlayerStats? stats;
            if (ctx.Source == DataSource.HeyBox)
            {
                var resp = await NarakaApiClient.HeyBoxUserInfoAsync(ctx.RoleIdSimple, ct).ConfigureAwait(false);
                stats = UnifiedMapper.MapHeyBoxStats(resp);
            }
            else
            {
                var resp = await NarakaApiClient.GetPlayerStatsAsync(ctx.RoleIdSimple, seasonId, gameMode, ct).ConfigureAwait(false);
                stats = UnifiedMapper.MapMiniProgramStats(resp);
            }

            if (stats?.Stats == null) return null;

            var result = new PlayerStatsLoadResult();
            foreach (var stat in stats.Stats)
            {
                if (string.IsNullOrEmpty(stat.Key)) continue;
                var val = string.IsNullOrEmpty(stat.Value) ? "-" : stat.Value;
                result.Stats[stat.Key] = val;
                // miniProgram 分支用英文 key 命中；heyBox 分支用中文 desc（作为 Key 存入）命中中文分支
                switch (stat.Key)
                {
                    case "avg_kill":
                    case "场均击杀": result.AvgKill = val; break;
                    case "top5_rate":
                    case "前五率": result.Top5Rate = val; break;
                    case "avg_damage":
                    case "场均伤害": result.AvgDamage = val; break;
                    case "avg_total_live_time":
                    case "场均存活时间": result.SurviveTime = FormatSurvivalTime(val); break;
                }
            }

            if (stats.Grade != null)
            {
                result.RankName = stats.Grade.GradeName;
                result.RankIcon = stats.Grade.GradeIcon;
                result.RankScore = stats.Grade.GradeScore;
                var gm = (int)gameMode;
                result.PageRankName = GetRankNameForScore(result.RankScore, gm) + GetSubTierName(result.RankScore, gm);
                result.PageStarCount = GetStarCount(result.RankScore, gm);
                result.PageHasStars = ((GameMode)gm).IsRankMode() && result.RankScore >= 4500;
                result.RankTierScore = GetRankTierScore(result.RankScore, gm);
            }

            return result;
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

        // 资源键命名约定：Rank.<拼音>。fallback 为中文原文，防止资源未配置时回退到键名或空串。
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

        public static double GetRankTierScore(double score, int gameMode = 0)
        {
            if (!((GameMode)gameMode).IsRankMode()) return score;
            if (score >= 4500) return (score - 4500) % 100;
            if (score >= 4000) return (score - 4000) % 100;
            if (score >= 3500) return (score - 3500) % 100;
            if (score >= 3000) return (score - 3000) % 100;
            if (score >= 2500) return (score - 2500) % 100;
            if (score >= 2000) return (score - 2000) % 100;
            if (score >= 1500) return (score - 1500) % 100;
            if (score >= 1000) return (score - 1000) % 100;
            return 0;
        }

        /// <summary>
        /// 获取子段位名称（五、四、三、二、一），每小段 100 分
        /// 仅对排位模式 1000~4499 分有效
        /// </summary>
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

        /// <summary>
        /// 获取段位的起始分数线（该大段的最低分），仅用于子段计算
        /// </summary>
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

    /// <summary>
    /// PlayerStatsLoader 的纯数据返回值。VM 把这些字段填回 TeamMemberInfo。
    /// </summary>
    public class PlayerStatsLoadResult
    {
        public Dictionary<string, string> Stats { get; } = new();
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
        public double RankTierScore { get; set; }
    }
}
