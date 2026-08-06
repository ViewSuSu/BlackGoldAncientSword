using BlackGoldAncientSword.Framework.Core.Consts;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    /// <summary>
    /// 排数（三排/双排/单排）下拉/单选项。DisplayName 走本地化资源 "GameMode.&lt;枚举名&gt;"，
    /// 语言切换后由承载它的 BindingList.ResetBindings() 触发重新读取。
    /// 供 <see cref="SeasonFilterBar"/> 及各页面共用，避免每页重复定义。
    /// </summary>
    public class TeamSizeOption
    {
        public TeamSize Value { get; }
        public TeamSizeOption(TeamSize value) => Value = value;
        public string DisplayName =>
            System.Windows.Application.Current?.TryFindResource("GameMode." + Value.ToString()) as string ?? Value.ToString();
    }

    /// <summary>
    /// 模式大类（天选/匹配/天人）下拉/单选项。DisplayName 走本地化资源 "GameMode.&lt;枚举名&gt;"。
    /// 供 <see cref="SeasonFilterBar"/> 及各页面共用。
    /// </summary>
    public class GameModeCategoryOption
    {
        public GameModeCategory Value { get; }
        public GameModeCategoryOption(GameModeCategory value) => Value = value;
        public string DisplayName =>
            System.Windows.Application.Current?.TryFindResource("GameMode." + Value.ToString()) as string ?? Value.ToString();
    }
}
