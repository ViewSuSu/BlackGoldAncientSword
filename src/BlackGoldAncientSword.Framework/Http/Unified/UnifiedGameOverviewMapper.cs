using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public static class UnifiedGameOverviewMapper
    {

        public static UnifiedGameOverview? Map(HeyboxGameDetailResponse? resp)
        {
            var detail = resp?.Result;
            if (detail is null) return null;

            var metrics = detail.UserNum?.GameData ?? new List<HeyboxGameDataItem>();

            return new UnifiedGameOverview
            {
                Name = detail.Name ?? string.Empty,
                NameEn = detail.NameEn ?? string.Empty,
                IconUrl = detail.Appicon ?? string.Empty,
                CoverUrl = detail.Image ?? string.Empty,
                Score = detail.Score ?? string.Empty,
                ScoreCommentCount = FormatCount(detail.CommentStats?.ScoreComment),
                FollowText = detail.FollowNumStr ?? string.Empty,
                Tags = MapTags(detail.CommonTags),
                MediaThumbnails = MapThumbnails(detail.Screenshots),
                OnlineNow = Metric(metrics, d => d.Contains("在线", StringComparison.Ordinal) && !d.Contains("趋势", StringComparison.Ordinal)),
                OnlinePeak = Metric(metrics, d => d.Contains("峰值", StringComparison.Ordinal)),
                PlayerCount = Metric(metrics, d => d.Contains("玩家", StringComparison.Ordinal) && !d.Contains("在线", StringComparison.Ordinal)),
                ApprovalRate = Metric(metrics, d => d.Contains("好评", StringComparison.Ordinal)),
                SalesRank = Metric(metrics, d => d.Contains("销量", StringComparison.Ordinal)),
                AvgPlayTime = Metric(metrics, d => d.Contains("时长", StringComparison.Ordinal) || d.Contains("游戏时间", StringComparison.Ordinal)),
                OnlineTrend = MapTrend(metrics),
            };
        }

        private static string Metric(List<HeyboxGameDataItem> items, Func<string, bool> match)
        {
            var item = items.FirstOrDefault(i => match(i.Desc ?? string.Empty));
            return item is null ? string.Empty : RichTextOf(item);
        }

        private static string RichTextOf(HeyboxGameDataItem item)
        {
            var attrs = item.HbRichText?.Attrs;
            if (attrs is not null)
            {
                var text = string.Concat(attrs
                    .Where(a => string.Equals(a.Type, "text", StringComparison.Ordinal))
                    .Select(a => a.Text ?? string.Empty));
                if (text.Length > 0) return text;
            }

            return item.Value ?? string.Empty;
        }

        private static List<UnifiedGameTrendPoint> MapTrend(List<HeyboxGameDataItem> items)
        {
            var points = items
                .FirstOrDefault(i => (i.Desc ?? string.Empty).Contains("趋势", StringComparison.Ordinal))
                ?.PeakValues;
            if (points is null || points.Count == 0) return new List<UnifiedGameTrendPoint>();

            var result = new List<UnifiedGameTrendPoint>(points.Count);
            foreach (var point in points)
            {
                if (point.Time is not { } seconds || seconds <= 0) continue;
                var peak = UnifiedValueParser.ParseLooseNumber(point.PeakValue);
                if (peak <= 0) continue;

                result.Add(new UnifiedGameTrendPoint
                {
                    Date = DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(TimeSpan.FromHours(8)).Date,
                    Peak = peak,
                });
            }

            return result;
        }

        private static List<string> MapTags(List<HeyboxGameTag>? tags)
        {
            var result = new List<string>();
            if (tags is null) return result;

            foreach (var tag in tags)
            {
                AddTag(result, tag.Desc);
                if (tag.DescList is null) continue;
                foreach (var desc in tag.DescList) AddTag(result, desc);
            }

            return result;
        }

        private static void AddTag(List<string> target, string? raw)
        {
            var value = CleanTag(raw);
            if (value.Length == 0) return;
            if (target.Contains(value, StringComparer.Ordinal)) return;
            target.Add(value);
        }

        private static string CleanTag(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (char.IsControl(ch) || ((int)ch >= 0xE000 && (int)ch <= 0xF8FF)) continue;
                sb.Append(ch);
            }

            return sb.ToString().Trim();
        }


        private static List<string> MapThumbnails(List<HeyboxGameScreenshot>? screenshots)
        {
            var result = new List<string>();
            if (screenshots is null) return result;

            foreach (var shot in screenshots)
            {
                var url = shot.Thumbnail;
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (result.Contains(url, StringComparer.Ordinal)) continue;
                result.Add(url);
            }

            return result;
        }

        private static string FormatCount(double? value)
        {
            if (value is not { } count || count <= 0) return string.Empty;
            return ((long)count).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
