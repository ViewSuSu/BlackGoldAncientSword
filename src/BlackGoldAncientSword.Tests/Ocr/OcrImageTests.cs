using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// OCR integration test using the real PaddleOCR engine to recognize test images.
/// If Python/PaddleOCR is not configured in the runtime environment, the test
/// will pass silently (equivalent to skip). In Visual Studio, if a debug
/// exception dialog pops up, press "Continue" - the test will still pass.
/// </summary>
public class OcrImageTests
{
    private static string TestDataPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

    [Fact]
    public void Recognize_Test1_ReturnsExpectedText()
    {
        var imagePath = Path.Combine(TestDataPath, "test1.png");
        Assert.True(File.Exists(imagePath), $"Test image not found: {imagePath}");

        OcrEngine? engine = null;
        try
        {
            engine = new OcrEngine();
        }
        catch (InvalidOperationException)
        {
            // Python/PaddleOCR not configured, skip test
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
