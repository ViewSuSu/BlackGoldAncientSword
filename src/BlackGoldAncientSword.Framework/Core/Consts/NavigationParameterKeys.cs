namespace BlackGoldAncientSword.Framework.Core.Consts
{
    public static class NavigationParameterKeys
    {
        /// <summary>
        /// 导航到战绩页时指定要查询的目标玩家名。队友卡片点"查看战绩"时携带队友名，
        /// 避免借道会被 PlayerPrefsService.LoadAsync 重置的 Current.PlayerName。
        /// </summary>
        public const string TargetPlayerName = nameof(TargetPlayerName);

        /// <summary>目标玩家的 role_id 与 server：已经有身份时直接带上，战绩页就不用再搜一遍。</summary>
        public const string TargetRoleId = nameof(TargetRoleId);

        public const string TargetServer = nameof(TargetServer);

        /// <summary>卡片已经拿到的头像 / 等级 / 赛季列表 / 当前赛季：带上后战绩页直接渲染，不再请求。</summary>
        public const string TargetAvatar = nameof(TargetAvatar);

        public const string TargetLevel = nameof(TargetLevel);

        public const string TargetSeasons = nameof(TargetSeasons);

        public const string TargetSeasonKey = nameof(TargetSeasonKey);
    }
}
