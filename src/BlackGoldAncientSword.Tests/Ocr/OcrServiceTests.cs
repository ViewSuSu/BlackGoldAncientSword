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
    public async Task IOcrService_Mock_RecognizeBytes_ReturnsExpected()
    {
        var mock = new Mock<IOcrService>();
        var testBytes = new byte[] { 1, 2, 3 };

        mock.Setup(m => m.RecognizeAsync(testBytes))
            .ReturnsAsync(new List<OcrResult>());

        var results = await mock.Object.RecognizeAsync(testBytes);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task IOcrService_Mock_RecognizeTextBytes_ReturnsConcatenated()
    {
        var mock = new Mock<IOcrService>();
        var testBytes = new byte[] { 1, 2, 3 };

        mock.Setup(m => m.RecognizeTextAsync(testBytes))
            .ReturnsAsync("Hello World");

        var text = await mock.Object.RecognizeTextAsync(testBytes);

        Assert.Equal("Hello World", text);
    }
}
