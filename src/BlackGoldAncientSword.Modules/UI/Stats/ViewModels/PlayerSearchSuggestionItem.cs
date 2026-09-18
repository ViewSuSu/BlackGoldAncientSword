namespace BlackGoldAncientSword.Modules.UI.Stats.ViewModels
{
    /// <summary>
    /// 搜索框候选列表里的一行。数据来自搜索接口返回的单个候选，创建后不再变化，
    /// 因此只读、不继承 ViewModelBase（候选分页一次可能上屏几十行，避免无谓的通知订阅）。
    /// </summary>
    public sealed class PlayerSearchSuggestionItem
    {
        public string RoleId { get; init; } = string.Empty;

        public string Server { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string AvatarUrl { get; init; } = string.Empty;

        public string RankIconUrl { get; init; } = string.Empty;

        /// <summary>候选行右侧那块段位：段位名。接口没给就是空串，此时整块不占位。</summary>
        public string RankName { get; init; } = string.Empty;

        public bool HasRank => RankName.Length > 0;

        /// <summary>候选行第二行的次要信息：UID + 接口给出的其余字段，已按展示顺序拼好。</summary>
        public string MetaText { get; init; } = string.Empty;
    }
}
