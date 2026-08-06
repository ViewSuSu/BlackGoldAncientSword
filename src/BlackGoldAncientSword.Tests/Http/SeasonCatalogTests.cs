using System.Linq;
using BlackGoldAncientSword.Framework.Http.Unified;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 赛季静态目录与网页 H5 前端内嵌表一致性校验：
    /// 索引 0 为当前赛季（Code = CurrentSeasonCode），Code 逐项递减，赛季名顺序固定。
    /// </summary>
    public class SeasonCatalogTests
    {
        [Fact]
        public void All_FirstIsCurrentSeason_CodesStrictlyDescending()
        {
            var seasons = SeasonCatalog.All();

            Assert.NotEmpty(seasons);
            // 索引 0 是当前赛季（网页：千机赛季 = 9620021）
            Assert.Equal(SeasonCatalog.CurrentSeasonCode, seasons[0].Code);
            Assert.Equal("千机赛季", seasons[0].Name);

            // Code 严格逐 1 递减（与网页选赛季时 seasonCode 递减规律一致）
            for (int i = 1; i < seasons.Count; i++)
                Assert.Equal(seasons[i - 1].Code - 1, seasons[i].Code);
        }

        [Fact]
        public void All_MatchesWebSeasonList()
        {
            // 与网页下拉抓取到的 22 个赛季完全一致（顺序即码从高到低）
            var expected = new[]
            {
                "千机赛季", "天工赛季", "穿云赛季", "侠风赛季", "裂变赛季", "燃耀赛季",
                "梦华赛季", "永昼赛季", "长生赛季", "淬炼赛季", "明镜赛季", "天纵赛季",
                "山海赛季", "无常赛季", "苍莽赛季", "奇巧赛季", "辉光赛季", "无妄赛季",
                "凌霄赛季", "破阵赛季", "浪潮赛季", "先行者赛季",
            };
            var actual = SeasonCatalog.All().Select(s => s.Name).ToArray();
            Assert.Equal(expected, actual);
            // 最早的"先行者赛季"码 = 9620000
            Assert.Equal(9620000, SeasonCatalog.All().Last().Code);
        }
    }
}
