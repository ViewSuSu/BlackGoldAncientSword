using System;
using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed class UnifiedGameOverview
    {

        public string Name { get; init; } = string.Empty;

        public string NameEn { get; init; } = string.Empty;

        public string IconUrl { get; init; } = string.Empty;

        public string CoverUrl { get; init; } = string.Empty;

        public string Score { get; init; } = string.Empty;

        public string ScoreCommentCount { get; init; } = string.Empty;

        public string FollowText { get; init; } = string.Empty;

        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> MediaThumbnails { get; init; } = Array.Empty<string>();

        public string OnlineNow { get; init; } = string.Empty;

        public string OnlinePeak { get; init; } = string.Empty;

        public string PlayerCount { get; init; } = string.Empty;

        public string ApprovalRate { get; init; } = string.Empty;

        public string SalesRank { get; init; } = string.Empty;

        public string AvgPlayTime { get; init; } = string.Empty;

        public IReadOnlyList<UnifiedGameTrendPoint> OnlineTrend { get; init; } = Array.Empty<UnifiedGameTrendPoint>();

        public bool HasContent =>
            !string.IsNullOrEmpty(OnlineNow) ||
            !string.IsNullOrEmpty(OnlinePeak) ||
            OnlineTrend.Count > 0 ||
            !string.IsNullOrEmpty(PlayerCount);
    }

    public sealed class UnifiedGameTrendPoint
    {

        public DateTime Date { get; init; }

        public double Peak { get; init; }
    }
}
