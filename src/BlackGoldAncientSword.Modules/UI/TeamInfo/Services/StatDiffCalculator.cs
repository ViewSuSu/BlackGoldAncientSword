using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// 对比列里一格的计算结果：显示文本 + 颜色。
    /// <see cref="Text"/> 为空串表示**不可比**（任一侧缺值、或两侧单位不一致），
    /// 与"两侧真的相等"的 <c>"0"</c> 在界面上必须能区分开。
    /// </summary>
    public readonly record struct StatDiff(string Text, string Color)
    {
        /// <summary>不可比：留空，不显示数字，避免被误读成"打平"。</summary>
        public static StatDiff NotComparable => new(string.Empty, StatDiffCalculator.ColorNeutral);
    }

    /// <summary>
    /// 队伍对比表的差值与单位解析。
    /// <para>
    /// 小黑盒 <c>overview[].value</c> 是服务端**已格式化好的成品串**（<c>"3629"</c> / <c>"12.5%"</c> /
    /// <c>"1.0h"</c> / <c>"7.5min"</c> / <c>"2.8k"</c>）；直接 <c>double.TryParse</c> 会失败并落到 0，
    /// 于是两成员相减恒为 0，差值列永远显示灰色的 0。这里把数值与单位拆开：
    /// 单位一致才算差（并把单位原样带回显示，如 <c>"+0.5h"</c>），单位不一致或任一侧缺值一律判不可比。
    /// </para>
    /// <para>
    /// 不做单位换算（<c>"2.8k"</c> 不折成 2800）：差值按原单位显示才有意义（<c>"+0.4k"</c>），
    /// 一旦换算，单位后缀就再也对不上了。
    /// </para>
    /// </summary>
    public static class StatDiffCalculator
    {
        /// <summary>打平 / 不可比用的中性灰。</summary>
        public const string ColorNeutral = "#999999";

        /// <summary>A 高于 B。</summary>
        public const string ColorHigher = "#22AA22";

        /// <summary>A 低于 B。</summary>
        public const string ColorLower = "#DD3333";

        private const double Epsilon = 0.001;
        private const string NumberFormat = "0.##";

        /// <summary>数值 + 可选单位后缀（<c>"%"</c>/<c>"h"</c>/<c>"min"</c>/<c>"k"</c>/<c>"万"</c> …）。</summary>
        private static readonly Regex ValuePattern =
            new(@"^([-+]?[0-9]*\.?[0-9]+)\s*([^\d\s]*)$", RegexOptions.Compiled);

        /// <summary>
        /// 后端自身可能下发的中文时长格式（<c>"18分30秒"</c>）。前端不再自己产出这种格式，
        /// 但若服务端给了，仍要归一到分钟，才能与 <c>"7.5min"</c> 同量纲互相比较。
        /// </summary>
        private static readonly Regex SurvivalTimePattern =
            new(@"^(\d+)分(\d+)秒$", RegexOptions.Compiled);

        /// <summary>缺值占位：后端对无数据的指标下发 <c>"-"</c> / <c>"None"</c> / 空串。</summary>
        private static bool IsPlaceholder(string text) =>
            text.Length == 0
            || text == "-"
            || text == "--"
            || text.Equals("None", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 解析成品串 → (数值, 单位)。缺值占位与无法识别的文本返回 false（判不可比），不抛异常。
        /// </summary>
        public static bool TryParseValue(string? raw, out double value, out string unit)
        {
            value = 0;
            unit = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var text = raw.Trim();
            if (IsPlaceholder(text)) return false;

            // 中文时长：服务端偶尔直接给 "X分XX秒"，归一到分钟，
            // 与小黑盒原生的 "7.5min" 同一口径、可互相比较。
            var survival = SurvivalTimePattern.Match(text);
            if (survival.Success
                && int.TryParse(survival.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
                && int.TryParse(survival.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                value = minutes + seconds / 60d;
                unit = "min";
                return true;
            }

            var m = ValuePattern.Match(text);
            if (!m.Success) return false;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return false;

            value = parsed;
            unit = m.Groups[2].Value;
            return true;
        }

        /// <summary>成员卡上的两个原始成品串求差（A - B）。</summary>
        public static StatDiff FromValues(string? rawA, string? rawB)
        {
            if (!TryParseValue(rawA, out var a, out var unitA)) return StatDiff.NotComparable;
            if (!TryParseValue(rawB, out var b, out var unitB)) return StatDiff.NotComparable;
            // 单位不同就不在同一量纲上，硬减出来的数字没有意义。
            if (!string.Equals(unitA, unitB, StringComparison.OrdinalIgnoreCase)) return StatDiff.NotComparable;

            return FromNumbers(a, b, unitA);
        }

        /// <summary>
        /// 段位分等纯数值行求差（A - B）。任一侧 <c>&lt;= 0</c> 视为"未加载 / 无该模式战绩"
        /// （与值列显示 <c>"-"</c> 的口径一致），判不可比，避免拿 0 去减出一个假的巨大差值。
        /// </summary>
        public static StatDiff FromScores(double a, double b)
        {
            if (a <= 0 || b <= 0) return StatDiff.NotComparable;
            return FromNumbers(a, b, string.Empty);
        }

        private static StatDiff FromNumbers(double a, double b, string unit)
        {
            var diff = a - b;
            // 真的相等：显示 0（带单位，与同列其它差值同口径），中性灰。
            if (Math.Abs(diff) < Epsilon) return new StatDiff("0" + unit, ColorNeutral);

            var sign = diff > 0 ? "+" : string.Empty;   // 负数自带 "-"
            var text = sign + diff.ToString(NumberFormat, CultureInfo.InvariantCulture) + unit;
            return new StatDiff(text, diff > 0 ? ColorHigher : ColorLower);
        }
    }
}
