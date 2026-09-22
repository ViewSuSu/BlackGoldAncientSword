namespace BlackGoldAncientSword.Framework.Http.Heybox
{
    /// <summary>
    /// 当前登录账号绑定的角色信息：绑定弹窗「已绑定」视图展示用，
    /// 也是「查看战绩」直接跳转所需的最小数据集。
    /// </summary>
    public sealed record BoundRoleInfo(
        string RoleId,
        string Server,
        string ServerDesc,
        string Name,
        string Avatar,
        double Level);
}
