using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 白字二值化预处理实验：昵称是白字，其他颜色（图标/血条/装饰）视为噪声。
/// 按 min(B,G,R) &gt;= 阈值 判定"白" → 变纯白；其余 → 纯黑。
/// 之后反色为黑字白底喂给 OCR，理论上把干扰全部清零，只留昵称笔画。
/// 与现有反色 BMP 路径对比 R region 的幻影 `不` 是否消失。
/// </summary>
public class WhiteMaskOcrTests
{
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },
    };

    /// <summary>
    /// 多阈值扫描，看不同 min-channel 阈值对识别效果的影响。
    /// </summary>
    [Theory]
    [InlineData(180)]
    [InlineData(200)]
    [InlineData(220)]
    [InlineData(240)]
    public async Task WhiteMask_Threshold_Compare(int threshold)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "team_full_1.png");
        Assert.True(File.Exists(srcPath), $"源图不存在: {srcPath}");

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(srcPath);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();

        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        var rawBgra = new byte[w * 4 * h];
        converted.CopyPixels(rawBgra, w * 4, 0);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, $"team_full_1_whitemask_t{threshold}");
        Directory.CreateDirectory(outDir);

        using var engine = new OcrEngine();
        var labels = new[] { "L", "M", "R" };

        Console.WriteLine($"===== 阈值 min(B,G,R) >= {threshold} =====");

        for (int i = 0; i < TeamRegions.Length; i++)
        {
            var region = TeamRegions[i];
            int cropX = (int)(region.X * w);
            int cropY = (int)(region.Y * h);
            int cropW = (int)(region.Width * w);
            int cropH = (int)(region.Height * h);

            // 白字二值化 + 反色（→ 黑字白底 PNG）
            var maskedPng = WhiteMaskAndInvertToPng(rawBgra, w, cropX, cropY, cropW, cropH, threshold);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_whitemask_t{threshold}.png"), maskedPng);

            var results = await engine.RecognizeAsync(maskedPng);

            Console.WriteLine($"---- {labels[i]} region {cropW}x{cropH} ----");
            Console.WriteLine($"  行数={results.Count}  拼接='{string.Join("", results.Select(r => r.Text))}'");
            foreach (var r in results)
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}");
        }

        Console.WriteLine($"产物: {outDir}");
    }

    /// <summary>
    /// 与生产路径（CropAndBinarizeWhite）在同一张图上直接对比 R region，
    /// 一次跑完看幻影 `不` 是否消失。
    /// </summary>
    [Fact]
    public async Task WhiteMask_vs_ProductionInvert_SideBySide()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "team_full_1.png");
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(srcPath);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();

        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        var rawBgra = new byte[w * 4 * h];
        converted.CopyPixels(rawBgra, w * 4, 0);

        using var engine = new OcrEngine();
        var labels = new[] { "L", "M", "R" };
        const int Threshold = 200;

        for (int i = 0; i < TeamRegions.Length; i++)
        {
            var region = TeamRegions[i];
            int cropX = (int)(region.X * w);
            int cropY = (int)(region.Y * h);
            int cropW = (int)(region.Width * w);
            int cropH = (int)(region.Height * h);

            var (invBmp, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, w, h, region);
            var maskedPng = WhiteMaskAndInvertToPng(rawBgra, w, cropX, cropY, cropW, cropH, Threshold);

            var invRes = await engine.RecognizeAsync(invBmp!);
            var maskRes = await engine.RecognizeAsync(maskedPng);

            Console.WriteLine($"---- {labels[i]} ----");
            Console.WriteLine($"  生产反色    : '{string.Join("", invRes.Select(r => r.Text))}'  ({invRes.Count} block)");
            foreach (var r in invRes)
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}");
            Console.WriteLine($"  白字二值化  : '{string.Join("", maskRes.Select(r => r.Text))}'  ({maskRes.Count} block)");
            foreach (var r in maskRes)
                Console.WriteLine($"    · '{r.Text}' conf={r.Confidence:F3}");
        }
    }

    /// <summary>
    /// 按 min-channel 阈值把白字保留 (→ 0x00 黑,反色后 0xFF 白)，
    /// 其他像素判为噪声 (→ 0xFF 白,反色后 0x00 黑)。
    /// 最终输出黑底白字 PNG？ 不，OCR 训练分布是黑字白底 → 输出黑字白底。
    ///
    /// 步骤：
    ///   src 像素 min(B,G,R) &gt;= threshold → 判为"白字" → 输出黑 (0x00)
    ///   否则 → 判为"背景/噪声" → 输出白 (0xFF)
    /// 直接一步得到反色后的黑字白底，跳过独立反色 pass。
    /// </summary>
    private static byte[] WhiteMaskAndInvertToPng(
        byte[] rawBgra, int fullW, int x, int y, int w, int h, int threshold)
    {
        var buf = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * w * 4;
            for (int col = 0; col < w; col++)
            {
                byte B = rawBgra[srcOff + col * 4 + 0];
                byte G = rawBgra[srcOff + col * 4 + 1];
                byte R = rawBgra[srcOff + col * 4 + 2];
                byte minCh = Math.Min(B, Math.Min(G, R));

                byte v = minCh >= threshold ? (byte)0x00 : (byte)0xFF;
                buf[dstOff + col * 4 + 0] = v;
                buf[dstOff + col * 4 + 1] = v;
                buf[dstOff + col * 4 + 2] = v;
                buf[dstOff + col * 4 + 3] = 0xFF;
            }
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var sk = new SKBitmap();
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
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
