namespace BlackGoldAncientSword.GameMonitor.Models;

/// <summary>
/// CCMini 语音日志识别到队伍名单后触发的事件参数。
/// <see cref="TeammateUids"/> 为已去重的队友角色 UID 列表（不含本地用户）。
/// </summary>
public class CcMiniTeammatesEventArgs : EventArgs
{
    public IReadOnlyList<string> TeammateUids { get; init; } = Array.Empty<string>();
}
