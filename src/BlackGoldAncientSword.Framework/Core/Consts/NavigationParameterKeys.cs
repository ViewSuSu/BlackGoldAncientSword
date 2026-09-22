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

        /// <summary>
        /// 卡片已经拿到的段位展示字段（段位图标 / 段位名 / 段位分 / 上行段位名 / 星数 / 是否有星）。
        /// 段位卡的图标与名字只由 <c>home/data</c> 的 grade 填充，而卡片点详情时并不保证再拉一次该接口
        /// ——（同一玩家同赛季同排数时缓存命中会跳过），于是段位图标位置会是空的。把卡片已有的原值带上，
        /// 战绩页段位卡立刻有内容。
        /// </summary>
        public const string TargetRankIcon = nameof(TargetRankIcon);

        public const string TargetRankName = nameof(TargetRankName);

        public const string TargetRankScore = nameof(TargetRankScore);

        public const string TargetPageRankName = nameof(TargetPageRankName);

        public const string TargetPageStarCount = nameof(TargetPageStarCount);

        public const string TargetPageHasStars = nameof(TargetPageHasStars);

        public const string TargetSeasons = nameof(TargetSeasons);

        public const string TargetSeasonKey = nameof(TargetSeasonKey);

        /// <summary>
        /// 卡片当前查的是几排（<see cref="TeamSize"/>）：战绩页据此把排数单选切到同一个值，
        /// 保证「卡片看双排 → 点详情看到的也是双排」。同一份 <c>home/data</c> 已在提供者缓存里，
        /// 切换只是换个 <c>battle_tid</c> 取同一玩家的另一份数据，不会重复请求资料/赛季。
        /// </summary>
        public const string TargetTeamSize = nameof(TargetTeamSize);

        /// <summary>
        /// 导航到绑定角色弹窗时预填的游戏昵称（取战绩页搜索框当前内容，省得用户再输一遍）。
        /// </summary>
        public const string BindRoleGameId = nameof(BindRoleGameId);
    }
}
