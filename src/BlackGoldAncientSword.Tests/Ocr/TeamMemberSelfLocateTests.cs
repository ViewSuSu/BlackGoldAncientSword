using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 锁定"本地用户必然居中"的定位契约（<see cref="TeamMemberNameCorrector.FindSelfIndex"/>）。
/// 回归背景：旧实现用精确字符串匹配定位本地用户，OCR 把本地名识别错（相似度不过
/// <see cref="TeamMemberNameCorrector.SelfMatchThreshold"/>）时匹配失败，本地用户漏到最右卡片。
/// FindSelfIndex 用 argmax 无阈值定位，保证总能挑出"最像自己"的那一格移到中间。
/// </summary>
public class TeamMemberSelfLocateTests
{
    [Fact]
    public void ExactMatch_ReturnsThatSlot()
    {
        var names = new[] { "队友甲", "本地玩家", "队友乙" };
        Assert.Equal(1, TeamMemberNameCorrector.FindSelfIndex(names, "本地玩家"));
    }

    [Fact]
    public void LocalUserAtRightSlot_IsLocatedForReorder()
    {
        // 本地用户在最右（index 2），必须能被定位出来以便移到中间。
        var names = new[] { "队友甲", "队友乙", "本地玩家" };
        Assert.Equal(2, TeamMemberNameCorrector.FindSelfIndex(names, "本地玩家"));
    }

    [Fact]
    public void OcrGarbledBelowThreshold_StillLocatesSelf()
    {
        // OCR 把本地名 "小小椰子" 识别成丢失首尾装饰点 + 形近字的 "小小椰"（相似度可能不过阈值），
        // 但它仍是三格里最像本地名的一格 → FindSelfIndex 必须返回该下标（无阈值）。
        var names = new[] { "野排牢张", "小小椰", "酉红市炒蛋" };
        Assert.Equal(1, TeamMemberNameCorrector.FindSelfIndex(names, "小小椰子"));
    }

    [Fact]
    public void LocalNameMisreadFewChars_LocatesSelfNotTeammate()
    {
        // 本地名 "野排牢张" 被 OCR 错 1 字识成 "野排年张"（形近字 牢→年），队伍另两格是真实队友。
        // 相似度 argmax 必须命中被识错的本地格（0），不误选任何队友格。
        var names = new[] { "野排年张", "一刀两断", "路人甲乙" };
        Assert.Equal(0, TeamMemberNameCorrector.FindSelfIndex(names, "野排牢张"));
    }

    [Fact]
    public void LocalNameLostDecorativeDots_LocatesSelf()
    {
        // 本地名 ".小小椰子." 白字二值化丢首尾装饰点 → "小小椰子"，仍应命中该格。
        var names = new[] { "队友甲", "队友乙", "小小椰子" };
        Assert.Equal(2, TeamMemberNameCorrector.FindSelfIndex(names, ".小小椰子."));
    }

    [Fact]
    public void CompletelyDifferentNames_StillPicksArgmax()
    {
        // 即使全部很不像，也返回 argmax（≥0）而非 -1：本地用户必然在队伍中，必须落到某一格。
        var names = new[] { "AAAA", "BBBB", "本地玩家X" };
        var idx = TeamMemberNameCorrector.FindSelfIndex(names, "本地玩家");
        Assert.Equal(2, idx);
    }

    [Fact]
    public void EmptyLocalName_ReturnsMinusOne()
    {
        // 本地名为空 → 无法定位，返回 -1，调用方据此跳过重排（不误移队友）。
        var names = new[] { "队友甲", "队友乙" };
        Assert.Equal(-1, TeamMemberNameCorrector.FindSelfIndex(names, ""));
    }

    [Fact]
    public void EmptyNames_ReturnsMinusOne()
    {
        Assert.Equal(-1, TeamMemberNameCorrector.FindSelfIndex(System.Array.Empty<string>(), "本地玩家"));
    }
}
