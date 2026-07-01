using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 验证 PP-OCRv5 对特殊标点（`丶` 汉字点、ASCII `.` 等）的识别效果。
/// 图片来自实际游戏昵称截图，测试仅打印结果、不做硬断言，
/// 用于观察当前模型 + 参数下的实际识别情况。
/// </summary>
public class SpecialCharOcrTests
{
    public static IEnumerable<object[]> SpecialCharImages => new[]
    {
        new object[] { "special_char_1.png" },
        new object[] { "special_char_2.png" },
    };

    [Theory]
    [MemberData(nameof(SpecialCharImages))]
    public async Task SpecialCharImage_OcrEngine_PrintRecognizedResult(string fileName)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var imagePath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        Assert.True(File.Exists(imagePath), $"测试图片不存在: {imagePath}");

        var imageBytes = await File.ReadAllBytesAsync(imagePath);

        using var engine = new OcrEngine();
        var results = await engine.RecognizeAsync(imageBytes);

        Console.WriteLine($"=== {fileName} ({imageBytes.Length:N0} bytes) ===");
        Console.WriteLine($"识别行数: {results.Count}");

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var codepoints = string.Join(",", r.Text.Select(ch => $"U+{(int)ch:X4}"));
            Console.WriteLine(
                $"[{i}] Text='{r.Text}'  Conf={r.Confidence:F3}  " +
                $"Box=TL({r.Box.TopLeft.X},{r.Box.TopLeft.Y})-BR({r.Box.BottomRight.X},{r.Box.BottomRight.Y})  " +
                $"Codepoints=[{codepoints}]");
        }

        var joined = string.Join("", results.Select(r => r.Text));
        Console.WriteLine($"拼接: [{joined}]");
        Console.WriteLine($"含 '丶'(U+4E36)? {joined.Contains('丶')}");
        Console.WriteLine($"含 '、'(U+3001)? {joined.Contains('、')}");
        Console.WriteLine($"含 '.'(U+002E)?  {joined.Contains('.')}");
        Console.WriteLine($"含 '。'(U+3002)? {joined.Contains('。')}");
    }
}
