using BlackGoldAncientSword.Framework.Core.Consts;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// SearchRecord 归一化结果。DataSource 决定后续接口路径分派。
    /// </summary>
    public sealed class UnifiedSearchResult
    {
        public string RoleIdSimple { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        public string Avatar { get; init; } = string.Empty;
        public double RoleLevel { get; init; }
        public DataSource DataSource { get; init; }
        public string LevelName { get; init; } = string.Empty;
        public string LevelImg { get; init; } = string.Empty;
    }
}
