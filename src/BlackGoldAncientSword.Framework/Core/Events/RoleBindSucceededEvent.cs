using BlackGoldAncientSword.Framework.Http.Heybox;

namespace BlackGoldAncientSword.Framework.Core.Events
{
    /// <summary>
    /// 绑定角色成功事件。
    /// <para>
    /// 对齐网页端：绑定成功后**不带 role_id** 重拉一次主页数据，服务端按账号绑定关系返回刚绑定的角色
    /// （<see cref="BoundRole"/>，绑定页在发事件前查好；快照查询失败时为 null）。
    /// 订阅方（战绩页）优先用快照直接渲染（同队伍卡片的快照跳转路径），拿不到快照时退回按
    /// <see cref="GameId"/> 搜索——搜索接口只吃昵称、有大小写 / 简繁差异，刚绑定的角色有搜不到的风险。
    /// </para>
    /// </summary>
    public class RoleBindSucceededEventArgs
    {
        /// <summary>绑定用的游戏昵称（快照拿不到时的兜底查询条件）。</summary>
        public string GameId { get; }

        /// <summary>绑定成功后的角色快照；查询失败时为 null。</summary>
        public BoundRoleInfo? BoundRole { get; }

        public RoleBindSucceededEventArgs(string gameId, BoundRoleInfo? boundRole = null)
        {
            GameId = gameId;
            BoundRole = boundRole;
        }
    }

    /// <summary>
    /// 绑定角色成功。订阅方（战绩页）据此清缓存并展示刚绑定的角色——绑定前它查不到，绑定后服务端才有数据。
    /// </summary>
    public class RoleBindSucceededEvent : PubSubEvent<RoleBindSucceededEventArgs> { }
}
