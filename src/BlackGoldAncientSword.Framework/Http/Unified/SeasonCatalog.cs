using System.Collections.Generic;
using System.Linq;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 赛季静态目录。与网页 H5 前端一致：赛季列表为前端内嵌（后端无独立 seasons 接口），
    /// 赛季码从当前赛季 <see cref="CurrentSeasonCode"/> 严格递减，索引 0 为当前赛季。
    /// unified/season 接口的 seasonCode 参数取自本表 <see cref="UnifiedSeason.Code"/>。
    /// 出新赛季时在 <see cref="Names"/> 头部追加新赛季名并令 <see cref="CurrentSeasonCode"/> +1。
    /// </summary>
    public static class SeasonCatalog
    {
        /// <summary>当前（最新）赛季码。对应 <see cref="Names"/>[0]。</summary>
        public const int CurrentSeasonCode = 9620021;

        /// <summary>赛季名，索引 0 为当前赛季，依次向历史递减。顺序与网页下拉一致。</summary>
        private static readonly string[] Names =
        {
            "千机赛季", "天工赛季", "穿云赛季", "侠风赛季", "裂变赛季", "燃耀赛季",
            "梦华赛季", "永昼赛季", "长生赛季", "淬炼赛季", "明镜赛季", "天纵赛季",
            "山海赛季", "无常赛季", "苍莽赛季", "奇巧赛季", "辉光赛季", "无妄赛季",
            "凌霄赛季", "破阵赛季", "浪潮赛季", "先行者赛季",
        };

        /// <summary>生成完整赛季列表（索引 0 = 当前赛季，Code 递减）。</summary>
        public static List<UnifiedSeason> All()
            => Names.Select((name, i) => new UnifiedSeason
            {
                Code = CurrentSeasonCode - i,
                Name = name,
            }).ToList();
    }
}
