using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

public class TeamInfoOcrServiceImageTests
{
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },
    };

    [Fact]
    public void LiveCapture_TeamInfo_OCR_PrintResults()
    {
        // 确保控制台 UTF-8 输出
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 1. 截取游戏窗口
        var capture = new BlackGoldAncientSword.ScreenCapture.ScreenCaptureService();
        if (!capture.TryFindGameWindow("NarakaBladepoint", out var hwnd))
        {
            Console.WriteLine("游戏未运行。");
            return;
        }

        byte[] rawBgra;
        int fullWidth, fullHeight;
        try
        {
            rawBgra = capture.CaptureFullRaw(hwnd, out fullWidth, out fullHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"截图失败: {ex.Message}");
            return;
        }

        Console.WriteLine($"截图尺寸: {fullWidth}x{fullHeight}");

        // 2. 保存完整截图到桌面
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var fullPath = Path.Combine(desktop, "LiveCapture_Full.png");
        using (var ms = new MemoryStream())
        {
            var bitmap = BitmapSource.Create(
                fullWidth, fullHeight, 96, 96,
                PixelFormats.Bgra32, null, rawBgra, fullWidth * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
            File.WriteAllBytes(fullPath, ms.ToArray());
        }
        Console.WriteLine($"完整截图已保存: {fullPath}");

        // 3. 裁剪三个队友名字区域并 OCR
        var engine = new OcrEngine();
        var labels = new[] { "left", "middle", "right" };
        var labelNames = new[] { "左侧", "中间", "右侧" };
        var ocrResults = new System.Text.StringBuilder();

        for (int i = 0; i < TeamRegions.Length; i++)
        {
            // 保存原始裁剪图（无反色）供视觉检查
            var (rawPng, _, _) = TeamInfoOcrService.CropRegion(
                rawBgra, fullWidth, fullHeight, TeamRegions[i]);
            if (rawPng != null)
            {
                File.WriteAllBytes(Path.Combine(desktop, $"LiveCapture_Crop_{labels[i]}.png"), rawPng);
            }

            // OCR 使用反色版本
            var (pngBytes, cropW, cropH) = TeamInfoOcrService.CropAndInvert(
                rawBgra, fullWidth, fullHeight, TeamRegions[i]);

            if (pngBytes == null)
            {
                Console.WriteLine($"区域 {labelNames[i]}: 裁剪失败");
                continue;
            }

            var results = engine.Recognize(pngBytes);
            var name = string.Join("", results.Select(r => r.Text)).Trim().Replace(" ", "");
            var detail = string.Join(" | ", results.Select(r => $"'{r.Text}'(置信度:{r.Confidence:F2})"));

            var line = $"区域 {labelNames[i]} ({cropW}x{cropH}): 识别结果=[{name}]  详情=[{detail}]";
            Console.WriteLine(line);
            ocrResults.AppendLine(line);

        }

        var resultPath = Path.Combine(desktop, "LiveCapture_OCR_Result.txt");
        File.WriteAllText(resultPath, ocrResults.ToString(), System.Text.Encoding.UTF8);
        Console.WriteLine($"OCR 结果已保存: {resultPath}");

        engine.Dispose();
        capture.Dispose();
    }
}
