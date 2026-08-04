namespace BlackGoldAncientSword.Framework.Core.Consts
{
    public static class NavigationParameterKeys
    {
        /// <summary>
        /// 导航到战绩页时指定要查询的目标玩家名。队友卡片点"查看战绩"时携带队友名，
        /// 避免借道会被 PlayerPrefsService.LoadAsync 重置的 Current.PlayerName。
        /// </summary>
        public const string TargetPlayerName = nameof(TargetPlayerName);
    }
}
