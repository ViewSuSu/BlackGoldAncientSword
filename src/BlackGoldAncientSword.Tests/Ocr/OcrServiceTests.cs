using BlackGoldAncientSword.Ocr;
using Moq;

namespace BlackGoldAncientSword.Tests.Ocr;

public class OcrServiceTests
{
    [Fact]
    public void OcrResult_DefaultValues_AreSane()
    {
        var result = new OcrResult();

        Assert.Equal("", result.Text);
        Assert.Equal(0.0, result.Confidence);
        Assert.NotNull(result.Box);
    }

    [Fact]
    public void OcrBox_DefaultCorners_AreZero()
    {
        var box = new OcrBox();

        Assert.NotNull(box.TopLeft);
        Assert.NotNull(box.BottomRight);
        Assert.Equal(0, box.TopLeft.X);
        Assert.Equal(0, box.TopLeft.Y);
    }

    [Fact]
    public void OcrPoint_Constructor_SetsCoordinates()
    {
        var point = new OcrPoint(100, 200);

        Assert.Equal(100, point.X);
        Assert.Equal(200, point.Y);
    }

    [Fact]
    public void IOcrService_Mock_Recognize_ReturnsExpected()
    {
        var mock = new Mock<IOcrService>();
        var expected = new List<OcrResult>
        {
            new() { Text = "test", Confidence = 0.99 },
        };

        mock.Setup(m => m.Recognize("test.png")).Returns(expected);

        var results = mock.Object.Recognize("test.png");

        Assert.Single(results);
        Assert.Equal("test", results[0].Text);
        Assert.Equal(0.99, results[0].Confidence);
    }

    [Fact]
    public void IOcrService_Mock_RecognizeBytes_ReturnsExpected()
    {
        var mock = new Mock<IOcrService>();
        var testBytes = new byte[] { 1, 2, 3 };

        mock.Setup(m => m.Recognize(testBytes))
            .Returns(new List<OcrResult>());

        var results = mock.Object.Recognize(testBytes);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void IOcrService_Mock_RecognizeText_ReturnsConcatenated()
    {
        var mock = new Mock<IOcrService>();

        mock.Setup(m => m.RecognizeText("test.png"))
            .Returns("Hello World");

        var text = mock.Object.RecognizeText("test.png");

        Assert.Equal("Hello World", text);
    }
}
