using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Http.Heybox;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed record PlayerSourceContext(string RoleId, string Server)
    {

        public DataSource Source => DataSource.HeyBox;

        public static PlayerSourceContext FromRoleId(string roleId)
            => new(roleId, ServerFromRoleId(roleId));

        public static string ServerFromRoleId(string roleId)
            => !string.IsNullOrEmpty(roleId) && roleId.Length >= 3
               && int.TryParse(roleId.AsSpan(roleId.Length - 3), out _)
                ? roleId[^3..]
                : HeyboxRequestParams.DefaultServer;
    }
}
