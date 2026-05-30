using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

public class TeamInfoOcrTests
{
    private static string TestDataPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

    private static readonly HashSet<string> ExpectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "铁小驴", "花心超人本超", "野排牢张"
    };

    /// <summary>
    /// 与 TeamInfoOcrService 中定义相同的三个区域
    /// </summary>
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },  // 左侧
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },  // 中间
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },  // 右侧
    };

    private static readonly string[] RegionLabels = { "left", "middle", "right" };

    [Fact]
    public void Recognize_英雄选择队伍信息_识别三个队友名字()
    {
        var imagePath = Path.Combine(TestDataPath, "hero_selection_team.png");
        Assert.True(File.Exists(imagePath), $"测试图片不存在: {imagePath}");

        OcrEngine? engine = null;
        try
        {
            engine = new OcrEngine();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            // 加载图片获取原始 BGRA 像素
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            int fullWidth = bitmap.PixelWidth;
            int fullHeight = bitmap.PixelHeight;
            var stride = (fullWidth * bitmap.Format.BitsPerPixel + 7) / 8;
            var rawBgra = new byte[stride * fullHeight];
            bitmap.CopyPixels(rawBgra, stride, 0);

            var recognizedNames = new List<string>();

            for (int i = 0; i < TeamRegions.Length; i++)
            {
                // 复用 TeamInfoOcrService 的裁剪+反色逻辑
                var (pngBytes, cropW, cropH) = TeamInfoOcrService.CropAndInvert(
                    rawBgra, fullWidth, fullHeight, TeamRegions[i]);

                Assert.NotNull(pngBytes);
                Assert.True(cropW > 0 && cropH > 0,
                    $"Region {RegionLabels[i]} crop is zero-size");

                // 保存裁剪图片用于调试
                var cropPath = Path.Combine(TestDataPath, $"crop_{RegionLabels[i]}.png");
                File.WriteAllBytes(cropPath, pngBytes);

                // OCR 识别
                var results = engine.Recognize(cropPath);
                var rawText = string.Join("", results.Select(r => r.Text));
                var name = rawText.Trim().Replace(" ", "");

                recognizedNames.Add(name);
            }

            Assert.Equal(3, recognizedNames.Count);

            foreach (var expected in ExpectedNames)
            {
                Assert.Contains(expected, recognizedNames);
            }
        }
        finally
        {
            engine.Dispose();
        }
    }
}
