using System;
using System.Globalization;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    internal static class UnifiedValueParser
    {

        public static double ParseLooseNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        public static double ParseLooseNumber(double? raw) => raw ?? 0;

        public static long ToUnixMs(long? unixSeconds)
            => unixSeconds is > 0 ? unixSeconds.Value * 1000L : 0;

        public static string FormatStatValue(double value)
            => value == Math.Floor(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
    }
}
