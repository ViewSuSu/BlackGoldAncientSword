using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 对桌面拷贝进来的 game_screenshot_3.png：
/// (1) 用代码原有的 3 个 Trio 归一化区域做一次 OCR
/// (2) 用红色边框像素检测出来的 3 个红框区域做一次 OCR
/// 把两套结果和逐区域像素误差一起打印出来。
/// </summary>
public class GameScreenshot3OcrTests
{
    // 代码里 WhiteBinarizeProdSimTests.TrioRegions 现有的 3 个归一化区域
    private static readonly OcrRegion[] ExistingRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },
    };

    // 通过扫描红色像素得到的红框位置（在 2560x1600 下）
    //   L: x=770  y=1456  w=284  h=54
    //   M: x=1204 y=1456  w=288  h=54
    //   R: x=1650 y=1456  w=304  h=54
    // 归一化后：
    private static readonly OcrRegion[] RedFrameRegions = new[]
    {
        new OcrRegion { X = 770  / 2560.0, Y = 1456 / 1600.0, Width = 284 / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = 1204 / 2560.0, Y = 1456 / 1600.0, Width = 288 / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = 1650 / 2560.0, Y = 1456 / 1600.0, Width = 304 / 2560.0, Height = 54 / 1600.0 },
    };

    // 在红框基础上 X 左扩 40px、Width 右扩 40px（合计每侧各扩 40px），目的把前后 `.` 装饰点包进来
    private static readonly OcrRegion[] WidenedRegions = new[]
    {
        new OcrRegion { X = (770  - 40) / 2560.0, Y = 1456 / 1600.0, Width = (284 + 80) / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = (1204 - 40) / 2560.0, Y = 1456 / 1600.0, Width = (288 + 80) / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = (1650 - 40) / 2560.0, Y = 1456 / 1600.0, Width = (304 + 80) / 2560.0, Height = 54 / 1600.0 },
    };

    private static readonly string[] Labels = { "L", "M", "R" };

    [Fact]
    public async Task Compare_ExistingRegions_vs_RedFrameRegions()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "game_screenshot_3.png");
        Assert.True(File.Exists(srcPath), $"测试图不存在: {srcPath}");

        var (raw, w, h) = LoadBgra(srcPath);
        Assert.Equal(2560, w);
        Assert.Equal(1600, h);

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "gs3_region_compare");
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine($"=== game_screenshot_3.png  ({w}x{h}) ===");
        sb.AppendLine("区域误差 (现有 - 红框, 像素):");
        for (int i = 0; i < 3; i++)
        {
            var e = ToAbs(ExistingRegions[i], w, h);
            var r = ToAbs(RedFrameRegions[i], w, h);
            sb.AppendLine($"  {Labels[i]}  现有=({e.x},{e.y},{e.w}x{e.h})  红框=({r.x},{r.y},{r.w}x{r.h})  "
                        + $"Δx={e.x - r.x:+#;-#;0} Δy={e.y - r.y:+#;-#;0} Δw={e.w - r.w:+#;-#;0} Δh={e.h - r.h:+#;-#;0}");
        }
        sb.AppendLine();

        for (int i = 0; i < 3; i++)
        {
            sb.AppendLine($"---- {Labels[i]} ----");

            var (existBmp, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(raw, w, h, ExistingRegions[i]);
            var (redBmp,   _, _) = TeamInfoOcrService.CropAndBinarizeWhite(raw, w, h, RedFrameRegions[i]);
            var (wideBmp,  _, _) = TeamInfoOcrService.CropAndBinarizeWhite(raw, w, h, WidenedRegions[i]);

            File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_existing.bmp"), existBmp!);
            File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_redframe.bmp"), redBmp!);
            File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_widened.bmp"), wideBmp!);

            var existRes = await engine.RecognizeAsync(existBmp!);
            var redRes   = await engine.RecognizeAsync(redBmp!);
            var wideRes  = await engine.RecognizeAsync(wideBmp!);

            var existText = string.Join("", existRes.Select(x => x.Text));
            var redText   = string.Join("", redRes.Select(x => x.Text));
            var wideText  = string.Join("", wideRes.Select(x => x.Text));

            sb.AppendLine($"  现有区域  [{existRes.Count} block]: '{existText}'");
            foreach (var b in existRes)
                sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");
            sb.AppendLine($"  红框区域  [{redRes.Count} block]: '{redText}'");
            foreach (var b in redRes)
                sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");
            sb.AppendLine($"  扩宽区域  [{wideRes.Count} block]: '{wideText}'");
            foreach (var b in wideRes)
                sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");
        }

        var text = sb.ToString();
        Console.WriteLine(text);
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), text, Encoding.UTF8);
        File.WriteAllText(Path.Combine(desktop, "gs3_region_compare_summary.txt"), text, Encoding.UTF8);
    }

    private static (int x, int y, int w, int h) ToAbs(OcrRegion r, int W, int H)
        => ((int)(r.X * W), (int)(r.Y * H), (int)(r.Width * W), (int)(r.Height * H));

    /// <summary>
    /// 在 <see cref="WidenedRegions"/> 上做两件事：
    /// (1) 导出裸色 PNG，肉眼确认前后 `.` 是否落在裁剪区里；
    /// (2) 用可调阈值白字二值化 (200/180/160/140/120) 逐档 OCR，看阈值是否是漏点主因。
    /// </summary>
    [Fact]
    public async Task Widened_RawDump_And_ThresholdSweep()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "game_screenshot_3.png");
        var (raw, w, h) = LoadBgra(srcPath);

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "gs3_threshold_sweep");
        Directory.CreateDirectory(outDir);

        int[] thresholds = { 200, 180, 160, 140, 120 };

        var sb = new StringBuilder();
        sb.AppendLine($"=== game_screenshot_3.png  ({w}x{h})  threshold sweep on widened region ===");

        for (int i = 0; i < 3; i++)
        {
            var abs = ToAbs(WidenedRegions[i], w, h);
            sb.AppendLine($"---- {Labels[i]}  abs=({abs.x},{abs.y},{abs.w}x{abs.h}) ----");

            // 1) 裸色 PNG
            var rawPng = CropRawToPng(raw, w, abs.x, abs.y, abs.w, abs.h);
            File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_raw.png"), rawPng);
            var rawRes = await engine.RecognizeAsync(rawPng);
            var rawText = string.Join("", rawRes.Select(x => x.Text));
            sb.AppendLine($"  裸色         [{rawRes.Count} block]: '{rawText}'");
            foreach (var b in rawRes)
                sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");

            // 2) 阈值扫描
            foreach (var t in thresholds)
            {
                var png = WhiteBinarizeToPng(raw, w, abs.x, abs.y, abs.w, abs.h, t);
                File.WriteAllBytes(Path.Combine(outDir, $"{Labels[i]}_thr{t:000}.png"), png);
                var res = await engine.RecognizeAsync(png);
                var text = string.Join("", res.Select(x => x.Text));
                sb.AppendLine($"  白二值化 t={t,3} [{res.Count} block]: '{text}'");
                foreach (var b in res)
                    sb.AppendLine($"    · '{b.Text}' conf={b.Confidence:F3}");
            }
            sb.AppendLine();
        }

        var txt = sb.ToString();
        Console.WriteLine(txt);
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), txt, Encoding.UTF8);
        File.WriteAllText(Path.Combine(desktop, "gs3_threshold_sweep_summary.txt"), txt, Encoding.UTF8);
    }

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

    private static byte[] WhiteBinarizeToPng(
        byte[] rawBgra, int fullW, int x, int y, int cw, int ch, int threshold)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * cw * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = rawBgra[srcOff + col * 4 + 0];
                byte G = rawBgra[srcOff + col * 4 + 1];
                byte R = rawBgra[srcOff + col * 4 + 2];
                byte v = Math.Min(B, Math.Min(G, R)) >= threshold ? (byte)0x00 : (byte)0xFF;
                buf[dstOff + col * 4 + 0] = v;
                buf[dstOff + col * 4 + 1] = v;
                buf[dstOff + col * 4 + 2] = v;
                buf[dstOff + col * 4 + 3] = 0xFF;
            }
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
