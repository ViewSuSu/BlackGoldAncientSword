using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// OCR 预处理组合矩阵实验：白字二值化 × 上采样倍数 × 边缘 padding。
/// 目标：在真实游戏截图上找到最稳的组合，包括超小字符（`丶`/`.`）。
///
/// 测试样本：
///   - team_full_1.png：完整 team info 截图（含幻影 `不` 干扰的 R region）
///   - special_char_1.png：`丶出现又离开` 手动 crop
///   - special_char_2.png：`云上也.` 手动 crop
///
/// 预处理组合：
///   [P0] baseline: 直接 OCR（对 special_char）/生产反色（对 full 截图）
///   [P1] 白字二值化 (thr=200)
///   [P2] P1 + 2× 上采样
///   [P3] P1 + 3× 上采样
///   [P4] P1 + 2× 上采样 + 20px 白 padding
///   [P5] P1 + 3× 上采样 + 30px 白 padding
/// </summary>
public class OcrPreprocessMatrixTests
{
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },
    };

    [Fact]
    public async Task PreprocessMatrix_TeamFull1_AllRegions()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", "team_full_1.png");
        var (rawBgra, w, h) = LoadBgra(srcPath);

        using var engine = new OcrEngine();
        var labels = new[] { "L", "M", "R" };
        var expected = new[] { "野排牢张", "九丁汉华", "日常耍废" };

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "team_full_1_matrix");
        Directory.CreateDirectory(outDir);

        var summary = new StringBuilder();
        summary.AppendLine($"图: team_full_1.png  期望: L={expected[0]}  M={expected[1]}  R={expected[2]}");
        summary.AppendLine();

        for (int i = 0; i < TeamRegions.Length; i++)
        {
            var region = TeamRegions[i];
            int cx = (int)(region.X * w), cy = (int)(region.Y * h);
            int cw = (int)(region.Width * w), ch = (int)(region.Height * h);

            summary.AppendLine($"---- {labels[i]} region {cw}x{ch}  期望='{expected[i]}' ----");

            // P0 生产反色
            var (invBmp, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, w, h, region);
            await RunAndReport(engine, invBmp!, $"P0 生产反色", expected[i], summary);

            // P1 白字二值化
            var p1 = WhiteMaskInvert(rawBgra, w, cx, cy, cw, ch, 200, 1, 0);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_P1.png"), p1);
            await RunAndReport(engine, p1, $"P1 白二值化(200)", expected[i], summary);

            // P2 + 2× 上采样
            var p2 = WhiteMaskInvert(rawBgra, w, cx, cy, cw, ch, 200, 2, 0);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_P2.png"), p2);
            await RunAndReport(engine, p2, $"P2 白+2x", expected[i], summary);

            // P3 + 3× 上采样
            var p3 = WhiteMaskInvert(rawBgra, w, cx, cy, cw, ch, 200, 3, 0);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_P3.png"), p3);
            await RunAndReport(engine, p3, $"P3 白+3x", expected[i], summary);

            // P4 + 2× + padding
            var p4 = WhiteMaskInvert(rawBgra, w, cx, cy, cw, ch, 200, 2, 20);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_P4.png"), p4);
            await RunAndReport(engine, p4, $"P4 白+2x+pad20", expected[i], summary);

            // P5 + 3× + padding
            var p5 = WhiteMaskInvert(rawBgra, w, cx, cy, cw, ch, 200, 3, 30);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_P5.png"), p5);
            await RunAndReport(engine, p5, $"P5 白+3x+pad30", expected[i], summary);

            summary.AppendLine();
        }

        Console.WriteLine(summary.ToString());
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), summary.ToString(), Encoding.UTF8);
    }

    [Theory]
    [InlineData("special_char_1.png", "丶出现又离开")]
    [InlineData("special_char_2.png", "云上也.")]
    public async Task PreprocessMatrix_SpecialChars(string fileName, string expected)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        var (rawBgra, w, h) = LoadBgra(srcPath);

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, $"{Path.GetFileNameWithoutExtension(fileName)}_matrix");
        Directory.CreateDirectory(outDir);

        var summary = new StringBuilder();
        summary.AppendLine($"图: {fileName}  尺寸: {w}x{h}  期望: '{expected}'");
        summary.AppendLine();

        // P0: 直接原图 OCR（无预处理）
        var rawPng = File.ReadAllBytes(srcPath);
        await RunAndReport(engine, rawPng, "P0 原图直入", expected, summary);

        // P1..P5: 全图（无 crop）走白字二值化 + 组合
        int fullX = 0, fullY = 0;
        var p1 = WhiteMaskInvert(rawBgra, w, fullX, fullY, w, h, 200, 1, 0);
        File.WriteAllBytes(Path.Combine(outDir, "P1.png"), p1);
        await RunAndReport(engine, p1, "P1 白二值化(200)", expected, summary);

        var p2 = WhiteMaskInvert(rawBgra, w, fullX, fullY, w, h, 200, 2, 0);
        File.WriteAllBytes(Path.Combine(outDir, "P2.png"), p2);
        await RunAndReport(engine, p2, "P2 白+2x", expected, summary);

        var p3 = WhiteMaskInvert(rawBgra, w, fullX, fullY, w, h, 200, 3, 0);
        File.WriteAllBytes(Path.Combine(outDir, "P3.png"), p3);
        await RunAndReport(engine, p3, "P3 白+3x", expected, summary);

        var p4 = WhiteMaskInvert(rawBgra, w, fullX, fullY, w, h, 200, 2, 20);
        File.WriteAllBytes(Path.Combine(outDir, "P4.png"), p4);
        await RunAndReport(engine, p4, "P4 白+2x+pad20", expected, summary);

        var p5 = WhiteMaskInvert(rawBgra, w, fullX, fullY, w, h, 200, 3, 30);
        File.WriteAllBytes(Path.Combine(outDir, "P5.png"), p5);
        await RunAndReport(engine, p5, "P5 白+3x+pad30", expected, summary);

        Console.WriteLine(summary.ToString());
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), summary.ToString(), Encoding.UTF8);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  预处理与辅助方法
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 白字二值化 (min(B,G,R) &gt;= threshold → 判为白字) + 反色 (→ 黑字白底) +
    /// nearest-neighbor 上采样 + 四周白 padding。输出 PNG 字节。
    /// </summary>
    private static byte[] WhiteMaskInvert(
        byte[] rawBgra, int fullW, int x, int y, int cw, int ch,
        int threshold, int scale, int padPx)
    {
        int outW = cw * scale + padPx * 2;
        int outH = ch * scale + padPx * 2;
        var buf = new byte[outW * outH * 4];

        // 先整块填白（作为反色背景 + padding）
        for (int idx = 0; idx < buf.Length; idx += 4)
        {
            buf[idx + 0] = 0xFF;
            buf[idx + 1] = 0xFF;
            buf[idx + 2] = 0xFF;
            buf[idx + 3] = 0xFF;
        }

        for (int row = 0; row < ch; row++)
        {
            int srcRowOff = (y + row) * fullW * 4 + x * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = rawBgra[srcRowOff + col * 4 + 0];
                byte G = rawBgra[srcRowOff + col * 4 + 1];
                byte R = rawBgra[srcRowOff + col * 4 + 2];
                byte minCh = Math.Min(B, Math.Min(G, R));
                bool isWhiteText = minCh >= threshold;

                if (!isWhiteText) continue;  // 保持默认白背景

                // 白字位置写黑（反色后的黑字）
                for (int sy = 0; sy < scale; sy++)
                {
                    int dstRow = (padPx + row * scale + sy) * outW * 4;
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int dstOff = dstRow + (padPx + col * scale + sx) * 4;
                        buf[dstOff + 0] = 0x00;
                        buf[dstOff + 1] = 0x00;
                        buf[dstOff + 2] = 0x00;
                        buf[dstOff + 3] = 0xFF;
                    }
                }
            }
        }

        return EncodePng(buf, outW, outH);
    }

    private static byte[] EncodePng(byte[] buf, int w, int h)
    {
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

    private static async Task RunAndReport(
        OcrEngine engine, byte[] imgBytes, string tag, string expected, StringBuilder sb)
    {
        var results = await engine.RecognizeAsync(imgBytes);
        var joined = string.Join("", results.Select(r => r.Text));
        var ok = joined == expected ? "✅" : (joined.Contains(expected) ? "◐" : "❌");
        sb.Append($"  [{tag,-18}] {ok}  '{joined}'  ({results.Count} block)  ");
        if (results.Count > 0)
        {
            var confs = string.Join(",", results.Select(r => r.Confidence.ToString("F2")));
            sb.Append($"conf=[{confs}]");
        }
        sb.AppendLine();
    }
}
