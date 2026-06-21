using System.Text;
using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

public class DiagnosticTests
{
    [Fact]
    public async Task DiagnoseCoords()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 检查 hero_selection_team.png 的分辨率
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "hero_selection_team.png");
        Assert.True(File.Exists(path));

        var bytes = await File.ReadAllBytesAsync(path);
        Console.Error.WriteLine($"hero_selection_team.png: {bytes.Length} bytes");

        using var engine = new OcrEngine();
        var results = await engine.RecognizeAsync(bytes);

        // 找 trio 名字的坐标
        var targetNames = new[] { "野排牢张", "叫我绪绪公主", "铁小驴", "花心超人本超" };
        foreach (var r in results)
        {
            if (targetNames.Any(n => r.Text.Contains(n)))
            {
                float nameY = (r.Box.TopLeft.Y + r.Box.BottomRight.Y) / 2f;
                int imgHeight = r.Box.BottomRight.Y > 0 ? r.Box.BottomRight.Y : 1152;
                Console.WriteLine($"  '{r.Text}' Y={nameY:F0} 归一化 Y={nameY/1152:F6}");
            }
        }

        // duo_screenshot.png 的分辨率
        var duoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "duo_screenshot.png");
        Assert.True(File.Exists(duoPath));
        var duoBytes = await File.ReadAllBytesAsync(duoPath);
        Console.Error.WriteLine($"\nduo_screenshot.png: {duoBytes.Length} bytes");

        var duoResults = await engine.RecognizeAsync(duoBytes);
        foreach (var r in duoResults)
        {
            if (targetNames.Any(n => r.Text.Contains(n)))
            {
                float nameY = (r.Box.TopLeft.Y + r.Box.BottomRight.Y) / 2f;
                Console.WriteLine($"  '{r.Text}' Y={nameY:F0} 归一化 Y={nameY/1600:F6}");
            }
        }
    }
}
