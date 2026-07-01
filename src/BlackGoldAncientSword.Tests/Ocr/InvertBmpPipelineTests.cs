using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 复现 TeamInfoOcrService 生产路径：对 hero_selection_team.png 走 CropAndBinarizeWhite，
/// 落盘反色 BMP + 直接 crop PNG 两份产物，同时喂给同一个 OcrEngine 对比识别结果。
/// 目的：确认反色/BMP 编码是否在超小字符 (`丶`/`.`) 上损失信号。
/// </summary>
public class InvertBmpPipelineTests
{
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },
    };

    [Fact]
    public async Task TeamPipeline_DumpCropVsInverted_AndCompareOcr()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "hero_selection_team.png");
        Assert.True(File.Exists(srcPath), $"源图不存在: {srcPath}");

        // 读入 BGRA raw（模拟 ScreenCaptureService 的输出）
        var uri = new Uri(srcPath);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = uri;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();

        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        int stride = w * 4;
        var rawBgra = new byte[stride * h];
        converted.CopyPixels(rawBgra, stride, 0);

        Console.WriteLine($"源图尺寸: {w}x{h}");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "team_pipeline_dump");
        Directory.CreateDirectory(outDir);

        using var engine = new OcrEngine();
        var labels = new[] { "L", "M", "R" };

        for (int i = 0; i < TeamRegions.Length; i++)
        {
            var region = TeamRegions[i];
            int cropX = (int)(region.X * w);
            int cropY = (int)(region.Y * h);
            int cropW = (int)(region.Width * w);
            int cropH = (int)(region.Height * h);

            // A) 团队生产路径：CropAndBinarizeWhite → 反色 BMP
            var (invertedBmp, iw, ih) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, w, h, region);
            Assert.NotNull(invertedBmp);
            var invPath = Path.Combine(outDir, $"{labels[i]}_inverted.bmp");
            File.WriteAllBytes(invPath, invertedBmp!);

            // 反色 BMP 再转 PNG 一份便于跨工具查看
            using (var sk = SKBitmap.Decode(invertedBmp))
            using (var img = SKImage.FromBitmap(sk))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(Path.Combine(outDir, $"{labels[i]}_inverted.png")))
            {
                data.SaveTo(fs);
            }

            // B) 对照：同一区域不反色，直接 crop 成 PNG
            var cropPng = CropToPng(rawBgra, w, h, cropX, cropY, cropW, cropH);
            var cropPath = Path.Combine(outDir, $"{labels[i]}_crop.png");
            File.WriteAllBytes(cropPath, cropPng);

            // OCR：两路都跑
            var invResults = await engine.RecognizeAsync(invertedBmp!);
            var cropResults = await engine.RecognizeAsync(cropPng);

            var invText = string.Join("", invResults.Select(r => r.Text));
            var cropText = string.Join("", cropResults.Select(r => r.Text));

            Console.WriteLine($"---- {labels[i]} ({iw}x{ih}) ----");
            Console.WriteLine($"  反色BMP  行数={invResults.Count}  拼接='{invText}'");
            foreach (var r in invResults)
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}");
            Console.WriteLine($"  原始crop 行数={cropResults.Count}  拼接='{cropText}'");
            foreach (var r in cropResults)
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}");
        }

        Console.WriteLine($"落盘目录: {outDir}");
    }

    /// <summary>把 raw BGRA 的一段区域直接编码为 PNG（无反色，纯 crop）。</summary>
    private static byte[] CropToPng(byte[] rawBgra, int fullW, int fullH, int x, int y, int w, int h)
    {
        var buf = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int srcRow = (y + row) * fullW * 4 + x * 4;
            int dstRow = row * w * 4;
            for (int col = 0; col < w; col++)
            {
                buf[dstRow + col * 4 + 0] = rawBgra[srcRow + col * 4 + 0];
                buf[dstRow + col * 4 + 1] = rawBgra[srcRow + col * 4 + 1];
                buf[dstRow + col * 4 + 2] = rawBgra[srcRow + col * 4 + 2];
                buf[dstRow + col * 4 + 3] = 0xFF;
            }
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var sk = new SKBitmap();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            sk.InstallPixels(info, handle.AddrOfPinnedObject(), w * 4);
            using var img = SKImage.FromBitmap(sk);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
