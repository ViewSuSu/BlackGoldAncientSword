using System;
using System.Collections.Generic;
using System.Reflection;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Services.Implementation;
using Moq;
using Xunit;

namespace BlackGoldAncientSword.Tests.GameMonitor;

/// <summary>
/// 验证 CcMiniTeammateMonitor.ExtractUids 的时间过滤逻辑：
/// 1) 新正则仍能正确解析真实格式的 set-uid-vol 行（回归保护）；
/// 2) matchStartTime 之后：早于它的旧局记录被丢弃；
/// 3) matchStartTime 为空：行为与修改前一致（全部保留）。
/// </summary>
public class CcMiniTeammateMonitorExtractUidsTests
{
    private const string OldLogLine =
        "[2026-08-09 12:17:18:529] [SERVICE] JsonControl {\"type\": \"set-uid-vol\", \"percent\" : 100, \"uid\" : \"jhc8000039740000080163\", \"session-id\" : 2 }";
    private const string NewLogLine =
        "[2026-08-09 12:53:38:735] [SERVICE] JsonControl {\"type\": \"set-uid-vol\", \"percent\" : 100, \"uid\" : \"1o51000145498000010163\", \"session-id\" : 2 }";
    private const string LocalUserLine =
        "[2026-08-09 12:17:18:529] [SERVICE] JsonControl {\"type\": \"set-uid-vol\", \"percent\" : 100, \"uid\" : \"l77c000015949400120163\", \"session-id\" : 2 }";
    private const string WrongSessionLine =
        "[2026-08-09 12:17:18:529] [SERVICE] JsonControl {\"type\": \"set-uid-vol\", \"percent\" : 100, \"uid\" : \"2a5d000045516500130163\", \"session-id\" : 1 }";

    private static List<string> ExtractUids(string text, DateTime? matchStartTime, string? localUid = null)
    {
        var prefs = new Mock<IPlayerPrefsService>();
        prefs.Setup(p => p.Current).Returns(new PlayerPrefsData { PlayerId = localUid ?? string.Empty });

        var monitor = new CcMiniTeammateMonitor(prefs.Object);
        // 通过 Reset 注入 matchStartTime（与生产调用路径一致）
        monitor.Reset(matchStartTime);

        var method = typeof(CcMiniTeammateMonitor)
            .GetMethod("ExtractUids", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (List<string>)method!.Invoke(monitor, new object[] { text })!;
    }

    [Fact]
    public void Regex_StillParsesRealLogLines()
    {
        var result = ExtractUids(OldLogLine + "\n" + NewLogLine, null);
        Assert.Contains("jhc8000039740000080163", result);
        Assert.Contains("1o51000145498000010163", result);
    }

    [Fact]
    public void WithMatchStart_FiltersOutOlderRecords()
    {
        // matchStart = 12:53，旧局（12:17）记录应被丢弃，新局（12:53）保留
        var result = ExtractUids(OldLogLine + "\n" + NewLogLine, new DateTime(2026, 8, 9, 12, 53, 0));
        Assert.DoesNotContain("jhc8000039740000080163", result);
        Assert.Contains("1o51000145498000010163", result);
    }

    [Fact]
    public void WithoutMatchStart_KeepsAllRecords()
    {
        var result = ExtractUids(OldLogLine + "\n" + NewLogLine, null);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FiltersLocalUser()
    {
        var result = ExtractUids(LocalUserLine, null, localUid: "l77c000015949400120163");
        Assert.Empty(result);
    }

    [Fact]
    public void FiltersWrongSession()
    {
        var result = ExtractUids(WrongSessionLine, null);
        Assert.Empty(result);
    }
}
