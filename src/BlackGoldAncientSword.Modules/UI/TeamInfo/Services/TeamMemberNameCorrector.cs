namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

/// <summary>
/// OCR 队伍名字后处理器：根据本地已知玩家昵称 (来自 player_prefs.txt)，
/// 把 OCR 结果里"最像本地玩家"的那一格覆盖为真名，其余队友名字保持原样。
/// <para>
/// 只允许校正本地玩家自身；任何情况下都不得修改队友名字。为此使用两道防护：
/// 1) argmax —— 只挑相似度最高的那一格；
/// 2) <see cref="SelfMatchThreshold"/> —— 最高相似度必须过阈值，否则整体不动。
/// </para>
/// <para>
/// 相似度采用归一化 Levenshtein 距离，大小写不敏感，中英数字符号通用。
/// </para>
/// </summary>
public static class TeamMemberNameCorrector
{
    /// <summary>
    /// 最像本地名的那一格必须达到此归一化相似度才校正。
    /// <para>
    /// 0.6 = 6 字符本地名允许最多 2 字符编辑距离。生产 OCR 主检测置信度 ≥ 0.85，
    /// 常见误识（形近字 <c>耍→要</c>、<c>贝→见</c>、装饰点丢失）单条编辑距离通常 ≤ 2，
    /// 相似度稳稳过 0.6；队友名与本地名撞相似度 ≥ 0.6 的概率极低，可安全避免误改。
    /// </para>
    /// </summary>
    public const double SelfMatchThreshold = 0.6;

    /// <summary>
    /// 若 OCR 结果中存在与 <paramref name="localName"/> 最相似且相似度过阈值的那一格，
    /// 返回一份新数组，仅将该格替换为 <paramref name="localName"/>；否则原样返回。
    /// </summary>
    /// <param name="names">OCR 识别出的队员名字数组，顺序对应槽位。</param>
    /// <param name="localName">本地玩家昵称，通常取 <c>PlayerPrefsData.OriginalPlayerName</c>。</param>
    /// <returns>校正后的名字数组；未命中校正条件时返回原引用。</returns>
    public static string[] Apply(string[] names, string? localName)
    {
        if (names == null || names.Length == 0) return names ?? System.Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(localName)) return names;

        // 已有一格与本地名精确相等 → OCR 完全正确，无需校正。
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], localName, System.StringComparison.OrdinalIgnoreCase))
                return names;
        }

        int bestIdx = -1;
        double bestSim = 0.0;
        for (int i = 0; i < names.Length; i++)
        {
            var sim = Similarity(names[i], localName);
            if (sim > bestSim)
            {
                bestSim = sim;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || bestSim < SelfMatchThreshold) return names;

        // 只克隆并改一格，其他槽位对象引用保持一致。
        var corrected = (string[])names.Clone();
        corrected[bestIdx] = localName!;
        return corrected;
    }

    /// <summary>
    /// 归一化 Levenshtein 相似度：<c>1 - editDistance / max(|a|, |b|)</c>，值域 [0, 1]。
    /// 大小写不敏感；任一串为空返回 0。使用滚动数组实现，O(n·m) 时间、O(m) 空间。
    /// </summary>
    internal static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        var la = a.ToLowerInvariant();
        var lb = b.ToLowerInvariant();
        int n = la.Length;
        int m = lb.Length;
        int maxLen = System.Math.Max(n, m);
        if (maxLen == 0) return 0.0;

        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = la[i - 1] == lb[j - 1] ? 0 : 1;
                int del = prev[j] + 1;
                int ins = curr[j - 1] + 1;
                int sub = prev[j - 1] + cost;
                curr[j] = System.Math.Min(System.Math.Min(del, ins), sub);
            }
            (prev, curr) = (curr, prev);
        }

        return 1.0 - (double)prev[m] / maxLen;
    }
}
