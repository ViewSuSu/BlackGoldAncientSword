using System;
using System.Collections.Generic;
using BlackGoldAncientSword.Framework.Core.Consts;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed class UnifiedSearchResult
    {
        public string RoleIdSimple { get; init; } = string.Empty;

        public string Server { get; init; } = string.Empty;

        public string RoleName { get; init; } = string.Empty;
        public string Avatar { get; init; } = string.Empty;
        public double RoleLevel { get; init; }
        public DataSource DataSource { get; init; } = DataSource.HeyBox;
        public string LevelName { get; init; } = string.Empty;
        public string LevelImg { get; init; } = string.Empty;

        public IReadOnlyList<UnifiedSearchField> Fields { get; init; } = Array.Empty<UnifiedSearchField>();
    }
}
