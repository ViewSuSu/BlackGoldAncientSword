using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

namespace BlackGoldAncientSword.Tests.UI.TeamInfo
{
    /// <summary>
    /// 队伍对比表差值计算：小黑盒 overview[].value 是服务端格式化好的成品串（"1.0h" / "7.5min" /
    /// "12.5%" / "2.8k"），必须剥掉单位才能相减；解析不出或单位对不齐时**留空**，
    /// 且必须与"两侧真的相等"的 "0" 在界面上可区分。
    /// </summary>
    public class StatDiffCalculatorTests
    {
        [Fact]
        public void FromValues_Unit_Is_Stripped_And_Carried_To_Result()
        {
            var diff = StatDiffCalculator.FromValues("12.5%", "10.0%");

            Assert.Equal("+2.5%", diff.Text);
            Assert.Equal(StatDiffCalculator.ColorHigher, diff.Color);
        }

        [Fact]
        public void FromValues_Time_Units_Are_Compared_Not_Zeroed()
        {
            var diff = StatDiffCalculator.FromValues("1.5h", "1.0h");

            Assert.Equal("+0.5h", diff.Text);
        }

        [Fact]
        public void FromValues_K_Suffix_Keeps_Original_Scale()
        {
            var diff = StatDiffCalculator.FromValues("3.2k", "2.8k");

            Assert.Equal("+0.4k", diff.Text);
        }

        [Fact]
        public void FromValues_Equal_Values_Show_Zero_Not_Blank()
        {
            var diff = StatDiffCalculator.FromValues("3629", "3629");

            Assert.Equal("0", diff.Text);
            Assert.NotEqual(string.Empty, diff.Text);
        }

        [Fact]
        public void FromValues_Missing_Value_Is_Not_Comparable()
        {
            // "-" 是后端"无数据"的占位，绝不能当成 0 去减出一个假的巨大差值。
            var diff = StatDiffCalculator.FromValues("-", "3629");

            Assert.Equal(string.Empty, diff.Text);
        }

        [Fact]
        public void FromValues_Mismatched_Units_Are_Not_Comparable()
        {
            var diff = StatDiffCalculator.FromValues("10500", "2.8k");

            Assert.Equal(string.Empty, diff.Text);
        }

        [Fact]
        public void FromValues_Chinese_Survival_Time_Matches_Minute_Unit()
        {
            // Loader 会把后端原始秒数格式化成 "18分30秒"，需与原生 "7.5min" 同一量纲才可比。
            var diff = StatDiffCalculator.FromValues("18分30秒", "10分00秒");

            Assert.Equal("+8.5min", diff.Text);
        }

        [Fact]
        public void FromScores_Unranked_Side_Is_Not_Comparable()
        {
            // 0 分 = 未加载 / 无该模式战绩，值列显示 "-"，差值列同样留空。
            Assert.Equal(string.Empty, StatDiffCalculator.FromScores(3629, 0).Text);
            Assert.Equal("+1079", StatDiffCalculator.FromScores(3629, 2550).Text);
        }

        [Fact]
        public void TryParseValue_Rejects_Placeholders_Without_Throwing()
        {
            Assert.False(StatDiffCalculator.TryParseValue(null, out _, out _));
            Assert.False(StatDiffCalculator.TryParseValue(string.Empty, out _, out _));
            Assert.False(StatDiffCalculator.TryParseValue("-", out _, out _));
            Assert.False(StatDiffCalculator.TryParseValue("None", out _, out _));
        }
    }
}
