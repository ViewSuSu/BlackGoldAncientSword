using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 用真实含 `丶` / `.` 前后缀昵称的完整游戏截图 (team_full_1.png)，
/// 复现团队路径（CropAndBinarizeWhite → 反色 BMP）与不反色 crop 两条路径的识别对比。
/// 每个 region 落盘反色 BMP + 反色 PNG + 原始 crop PNG，
/// 便于肉眼确认小字符是否清晰。
/// </summary>
public class TeamFull1PipelineTests
{
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },
    };

    [Fact]
    public async Task TeamFull1_InvertVsCrop_DumpAndCompareOcr()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "team_full_1.png");
        Assert.True(File.Exists(srcPath), $"源图不存在: {srcPath}");

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
        var outDir = Path.Combine(desktop, "team_full_1_dump");
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

            var (invertedBmp, iw, ih) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, w, h, region);
            Assert.NotNull(invertedBmp);

            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_inverted.bmp"), invertedBmp!);
            using (var sk = SKBitmap.Decode(invertedBmp))
            using (var img = SKImage.FromBitmap(sk))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(Path.Combine(outDir, $"{labels[i]}_inverted.png")))
            {
                data.SaveTo(fs);
            }

            var cropPng = CropToPng(rawBgra, w, h, cropX, cropY, cropW, cropH);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_crop.png"), cropPng);

            var invResults = await engine.RecognizeAsync(invertedBmp!);
            var cropResults = await engine.RecognizeAsync(cropPng);

            Console.WriteLine($"---- {labels[i]} region {cropW}x{cropH} @({cropX},{cropY}) ----");
            Console.WriteLine($"  反色BMP  行数={invResults.Count}  拼接='{string.Join("", invResults.Select(r => r.Text))}'");
            foreach (var r in invResults)
            {
                var cp = string.Join(",", r.Text.Select(ch => $"U+{(int)ch:X4}"));
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}  cp=[{cp}]");
            }
            Console.WriteLine($"  原始crop 行数={cropResults.Count}  拼接='{string.Join("", cropResults.Select(r => r.Text))}'");
            foreach (var r in cropResults)
            {
                var cp = string.Join(",", r.Text.Select(ch => $"U+{(int)ch:X4}"));
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}  cp=[{cp}]");
            }
        }

        Console.WriteLine($"产物: {outDir}");
    }

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
