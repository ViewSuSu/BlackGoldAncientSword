using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

/// <summary>
/// 队伍信息 OCR 识别服务。截取游戏窗口的队友名字区域，通过 PaddleOCR 识别玩家名称。
/// </summary>
public interface ITeamInfoOcrService
{
    /// <summary>
    /// 从当前游戏窗口中识别队友名字。
    /// 返回识别到的名字数组（已去重、去空格），最多 3 个成员。
    /// </summary>
    Task<string[]> RecognizeTeamMembersAsync(CancellationToken ct = default);
}
