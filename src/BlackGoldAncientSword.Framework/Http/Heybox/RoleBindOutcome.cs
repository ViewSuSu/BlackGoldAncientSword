namespace BlackGoldAncientSword.Framework.Http.Heybox
{
    /// <summary>
    /// 绑定角色的结果。<see cref="ErrorMessage"/> 非空时是后端下发的失败原因（原样展示）；
    /// 为空且失败时由 UI 层补默认文案（对齐网页端 bindFailed 的兜底分支）。
    /// </summary>
    public sealed record RoleBindOutcome(bool Success, string? ErrorMessage)
    {
        public static RoleBindOutcome Succeeded() => new(true, null);

        public static RoleBindOutcome Failed(string? message) => new(false, message);
    }
}
