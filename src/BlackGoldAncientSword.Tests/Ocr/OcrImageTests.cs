using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// OCR 集成测试，使用真实 PaddleOCR 引擎识别测试图片。
/// 如果运行环境中未配置 Python/PaddleOCR，测试将直接通过（等同于跳过）。
/// 在 VS 中调试时若弹出异常对话框，按"继续"即可，测试结果仍为通过。
/// </summary>
public class OcrImageTests
{
    private static string TestDataPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

    [Fact]
    public void Recognize_测试1_返回永劫无间查询战绩助手()
    {
        var imagePath = Path.Combine(TestDataPath, "test1.png");
        Assert.True(File.Exists(imagePath), $"测试图片不存在: {imagePath}");

        OcrEngine? engine = null;
        try
        {
            engine = new OcrEngine();
        }
        catch (InvalidOperationException)
        {
            // Python/PaddleOCR 未配置，测试跳过
            return;
        }

        try
        {
            var text = engine.RecognizeText(imagePath);
            Assert.Equal("永劫无间查询战绩助手", text.Trim());
        }
        finally
        {
            engine.Dispose();
        }
    }
}
