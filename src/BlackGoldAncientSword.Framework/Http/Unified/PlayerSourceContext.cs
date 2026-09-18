using BlackGoldAncientSword.Framework.Core.Consts;

namespace BlackGoldAncientSword.Framework.Http.Unified
{

    public sealed record PlayerSourceContext(string RoleId, string Server)
    {

        public DataSource Source => DataSource.HeyBox;
    }
}
