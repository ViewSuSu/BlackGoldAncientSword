using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 对桌面拷贝进来的 game_screenshot_1.png（2560x1600 英雄选择画面）跑 OCR。
/// 三档结果：现有归一化区域 / 裸色 / 白字二值化 threshold 200。
/// 输出到桌面 gs1_ocr_result 与 stdout 便于人工确认。
/// </summary>
public class GameScreenshot1OcrTests
{
    // 与 game_screenshot_3 相同的 3 排位归一化区域（英雄选择底部三格昵称）
    private static readonly OcrRegion[] TrioRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },
    };

    private static readonly string[] Labels = { "L", "M", "R" };

    [Fact]
    public async Task Recognize_Screenshot1_TrioNames()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "game_screenshot_1.png");
        Assert.True(File.Exists(srcPath), $"测试图不存在: {srcPath}");

        var (raw, w, h) = LoadBgra(srcPath);

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "gs1_ocr_result");
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine($"=== game_screenshot_1.png  ({w}x{h}) ===");

        for (int i = 0; i < 3; i++)
        {
            var abs = ToAbs(TrioRegions[i], w, h);
            sb.AppendLine($"---- {Labels[i]}  abs=({abs.x},{abs.y},{abs.w}x{abs.h}) ----");

            // 生产管线（白字二值化）
            var (prodBmp, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(raw, w, h, TrioRegions[i]);
            File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_prod.png"), prodBmp!);
            var prodRes = await engine.RecognizeAsync(prodBmp!);
            var prodText = string.Join("", prodRes.Select(x => x.Text));
            sb.AppendLine($"  生产管线 [{prodRes.Count} block]: '{prodText}'");
            foreach (var b in prodRes)
                sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");

            // 裸色对比
            var rawPng = CropRawToPng(raw, w, abs.x, abs.y, abs.w, abs.h);
            File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_raw.png"), rawPng);
            var rawRes = await engine.RecognizeAsync(rawPng);
            var rawText = string.Join("", rawRes.Select(x => x.Text));
            sb.AppendLine($"  裸色     [{rawRes.Count} block]: '{rawText}'");
            foreach (var b in rawRes)
                sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");
        }

        var text = sb.ToString();
        Console.WriteLine(text);
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), text, Encoding.UTF8);
        File.WriteAllText(Path.Combine(desktop, "gs1_ocr_summary.txt"), text, Encoding.UTF8);
    }

    private static (int x, int y, int w, int h) ToAbs(OcrRegion r, int W, int H)
        => ((int)(r.X * W), (int)(r.Y * H), (int)(r.Width * W), (int)(r.Height * H));

    private static byte[] CropRawToPng(byte[] rawBgra, int fullW, int x, int y, int cw, int ch)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * cw * 4;
            Array.Copy(rawBgra, srcOff, buf, dstOff, cw * 4);
        }
        return EncodePng(buf, cw, ch);
    }

    private static byte[] EncodePng(byte[] bgraBuf, int cw, int ch)
    {
        var info = new SKImageInfo(cw, ch, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var sk = new SKBitmap();
        var handle = GCHandle.Alloc(bgraBuf, GCHandleType.Pinned);
        try
        {
            sk.InstallPixels(info, handle.AddrOfPinnedObject(), cw * 4);
            using var img = SKImage.FromBitmap(sk);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally { handle.Free(); }
    }

    private static (byte[] raw, int w, int h) LoadBgra(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        var cv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = cv.PixelWidth, h = cv.PixelHeight;
        var buf = new byte[w * 4 * h];
        cv.CopyPixels(buf, w * 4, 0);
        return (buf, w, h);
    }
}
