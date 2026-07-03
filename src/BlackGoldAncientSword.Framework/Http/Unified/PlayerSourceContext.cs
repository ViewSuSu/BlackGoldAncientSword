using BlackGoldAncientSword.Framework.Core.Consts;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// SearchRecord 成功后由 VM 构造，锁定当前查询会话的角色 ID + 数据源。
    /// 后续所有 loader 调用都通过该 context 分派到对应的 miniProgram / heyBox 接口，
    /// 保证一次搜索期间不会中途换源。
    /// </summary>
    public sealed record PlayerSourceContext(string RoleIdSimple, DataSource Source);
}
