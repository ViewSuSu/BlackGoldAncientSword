using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

/// <summary>队伍信息 OCR 识别服务。截取游戏窗口的队友名字区域，通过 PaddleOCR 识别玩家名称。</summary>
public interface ITeamInfoOcrService
{
    /// <summary>从当前游戏窗口中识别三排队友名字。返回识别到的名字数组（已去重、去空格），最多 3 个成员。</summary>
    Task<string[]> RecognizeTeamMembersAsync(CancellationToken ct = default);

    /// <summary>从当前游戏窗口中识别双排队友名字。返回识别到的名字数组（已去重、去空格），最多 2 个成员。</summary>
    Task<string[]> RecognizeDuoTeamMembersAsync(CancellationToken ct = default);

    /// <summary>
    /// 自动检测队伍规模并识别队友名字。
    /// 先取三排区域左侧小块尝试识别，若能识别到有效文本则判为三排并走三排识别逻辑，
    /// 否则判为双排走双排识别逻辑。所有识别共用一次截图，无额外开销。
    /// </summary>
    Task<string[]> RecognizeTeamMembersAutoAsync(CancellationToken ct = default);
}

