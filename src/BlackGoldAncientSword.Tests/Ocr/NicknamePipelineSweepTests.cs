using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 反复扫描各种预处理管线，找出识别率最高的方案。
/// 覆盖 5 张现有 trio 图 + game_screenshot_3.png，每张图 L/M/R 三区域，
/// 每种管线记录：block 数、总 conf、拼接文本、是否命中已知正解。
/// </summary>
public class NicknamePipelineSweepTests
{
    private static readonly OcrRegion[] TrioRegions_2560x1600 = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },
    };
    // 扩宽 40px 左右各扩，绝对像素 (在 2560x1600 下)
    private static readonly OcrRegion[] WidenedRegions_2560x1600 = new[]
    {
        new OcrRegion { X = 730 / 2560.0,  Y = 1456 / 1600.0, Width = 364 / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = 1164 / 2560.0, Y = 1456 / 1600.0, Width = 368 / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = 1610 / 2560.0, Y = 1456 / 1600.0, Width = 384 / 2560.0, Height = 54 / 1600.0 },
    };

    /// <summary>已知正解（用户确认）。null 表示未知，仅打印，不计入准确率。</summary>
    private static readonly Dictionary<(string img, string label), string?> GroundTruth = new()
    {
        // 用户已确认
        [("game_screenshot_3.png", "L")] = null,
        [("game_screenshot_3.png", "M")] = ".小小椰子.",
        [("game_screenshot_3.png", "R")] = "酉红市炒蛋",
        [("team_full_1.png", "R")] = "日常耍废",
    };

    public static IEnumerable<object[]> AllImages => new[]
    {
        // 全部用 production 紧区（useWide=false）
        new object[] { "game_screenshot_3.png",     false },
        new object[] { "hero_selection_team.png",    false },
        new object[] { "team_full_1.png",            false },
        new object[] { "duo_screenshot.png",         false },
        new object[] { "duo_team_selection.png",     false },
    };

    private static readonly string[] Labels = { "L", "M", "R" };

    private enum PipelineKind { Raw, WhiteBinarize, GrayscaleInvertThreshold, OtsuInvert, RawConfFiltered, RawUnionWb }

    private record PipelineSpec(string Name, PipelineKind Kind, int Param, double ConfMin = 0);

    private static readonly PipelineSpec[] Pipelines = new[]
    {
        new PipelineSpec("raw",             PipelineKind.Raw, 0),
        new PipelineSpec("raw-cf75",        PipelineKind.RawConfFiltered, 0, 0.75),
        new PipelineSpec("raw-cf85",        PipelineKind.RawConfFiltered, 0, 0.85),
        new PipelineSpec("raw-cf90",        PipelineKind.RawConfFiltered, 0, 0.90),
        new PipelineSpec("wb-200",          PipelineKind.WhiteBinarize, 200),
        new PipelineSpec("wb-140",          PipelineKind.WhiteBinarize, 140),
        new PipelineSpec("otsu-inv",        PipelineKind.OtsuInvert, 0),
        new PipelineSpec("raw+wb200-union", PipelineKind.RawUnionWb, 200, 0.75),
        new PipelineSpec("union-prefer-wb", PipelineKind.RawUnionWb, 201, 0.75), // Param=201 → 走 prefer-wb 分支
        new PipelineSpec("raw-cf60",        PipelineKind.RawConfFiltered, 0, 0.60),
    };

    [Fact]
    public async Task SweepPipelines_AcrossAllTrioImages()
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "gs3_pipeline_sweep");
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine("=== Nickname Pipeline Sweep ===");

        // pipelineName -> (matches, phantomBlocks, confSum, evaluated)
        var totals = Pipelines.ToDictionary(p => p.Name, _ => (matches: 0, phantoms: 0, confSum: 0.0, evaluated: 0));
        int gtRegions = 0;

        foreach (var row in AllImages)
        {
            var fileName = (string)row[0];
            var useWide = (bool)row[1];

            var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
            if (!File.Exists(srcPath))
            {
                sb.AppendLine($"[skip] {fileName} 不存在");
                continue;
            }
            var (raw, w, h) = LoadBgra(srcPath);
            var regions = useWide ? WidenedRegions_2560x1600 : TrioRegions_2560x1600;

            sb.AppendLine();
            sb.AppendLine($"### {fileName}  ({w}x{h})  regions={(useWide ? "widened" : "existing")}");

            for (int i = 0; i < regions.Length; i++)
            {
                var abs = ToAbs(regions[i], w, h);
                var gtKey = (fileName, Labels[i]);
                GroundTruth.TryGetValue(gtKey, out var gt);
                if (gt != null) gtRegions++;
                sb.AppendLine($"-- {Labels[i]}  abs=({abs.x},{abs.y},{abs.w}x{abs.h})  正解={(gt ?? "?")}");

                foreach (var p in Pipelines)
                {
                    var (text, blockCount, avgConf) = await RunPipelineAsync(engine, raw, w, abs, p);
                    var mark = gt == null ? "  " : (text == gt ? "✓" : "✗");
                    sb.AppendLine($"   {p.Name,-16} [{blockCount}b] conf={avgConf:F3}  '{text}'  {mark}");

                    var t = totals[p.Name];
                    t.confSum += avgConf;
                    t.phantoms += Math.Max(0, blockCount - 1);
                    if (gt != null)
                    {
                        t.evaluated++;
                        if (text == gt) t.matches++;
                    }
                    totals[p.Name] = t;
                }

            }
        }

        sb.AppendLine();
        sb.AppendLine("=== 汇总 (all images × all regions) ===");
        sb.AppendLine($"{"pipeline",-14}  matches/eval={gtRegions}  phantomBlocks  avgConf");
        foreach (var p in Pipelines)
        {
            var t = totals[p.Name];
            int totalRegions = AllImages.Sum(r => {
                var img = (string)r[0];
                return File.Exists(Path.Combine(AppContext.BaseDirectory, "TestData", img)) ? 3 : 0;
            });
            var avgConf = totalRegions > 0 ? t.confSum / totalRegions : 0;
            sb.AppendLine($"{p.Name,-14}  {t.matches}/{t.evaluated,-2}         {t.phantoms,-3}           {avgConf:F3}");
        }

        var txt = sb.ToString();
        Console.WriteLine(txt);
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), txt, Encoding.UTF8);
        File.WriteAllText(Path.Combine(desktop, "gs3_pipeline_sweep_summary.txt"), txt, Encoding.UTF8);
    }

    private static (int x, int y, int w, int h) ToAbs(OcrRegion r, int W, int H)
        => ((int)(r.X * W), (int)(r.Y * H), (int)(r.Width * W), (int)(r.Height * H));

    /// <summary>跑单个管线，返回 (拼接文本, block 数, 平均置信度)。</summary>
    private static async Task<(string text, int blocks, double avgConf)> RunPipelineAsync(
        OcrEngine engine, byte[] raw, int w, (int x, int y, int w, int h) abs, PipelineSpec p)
    {
        // 双通道合并：raw ∪ wb
        if (p.Kind == PipelineKind.RawUnionWb)
        {
            bool preferWb = p.Param == 201;
            int wbThreshold = preferWb ? 200 : p.Param;
            var rawPng = CropRawToPng(raw, w, abs.x, abs.y, abs.w, abs.h);
            var wbPng  = WhiteBinarizeToPng(raw, w, abs.x, abs.y, abs.w, abs.h, wbThreshold);
            var rawRes = await engine.RecognizeAsync(rawPng);
            var wbRes  = await engine.RecognizeAsync(wbPng);
            var rawFiltered = rawRes.Where(x => x.Confidence >= p.ConfMin).ToList();
            var wbFiltered  = wbRes .Where(x => x.Confidence >= p.ConfMin).ToList();
            var rawText = string.Join("", rawFiltered.Select(x => x.Text));
            var wbText  = string.Join("", wbFiltered .Select(x => x.Text));
            var rawAvg = rawFiltered.Count > 0 ? rawFiltered.Average(x => x.Confidence) : 0;
            var wbAvg  = wbFiltered .Count > 0 ? wbFiltered .Average(x => x.Confidence) : 0;

            // 包含关系：取更长的
            if (rawText.Contains(wbText) && rawText.Length > wbText.Length && rawAvg >= 0.85)
                return (rawText, rawFiltered.Count, rawAvg);
            if (wbText.Contains(rawText) && wbText.Length > rawText.Length)
                return (wbText, wbFiltered.Count, wbAvg);
            // 都不包含：prefer-wb 模式下优先 wb，除非 wb 为空；否则按 conf
            if (preferWb)
                return wbFiltered.Count > 0 ? (wbText, wbFiltered.Count, wbAvg) : (rawText, rawFiltered.Count, rawAvg);
            return rawAvg >= wbAvg ? (rawText, rawFiltered.Count, rawAvg) : (wbText, wbFiltered.Count, wbAvg);
        }

        // 单通道
        byte[] png = p.Kind switch
        {
            PipelineKind.Raw or PipelineKind.RawConfFiltered => CropRawToPng(raw, w, abs.x, abs.y, abs.w, abs.h),
            PipelineKind.WhiteBinarize => WhiteBinarizeToPng(raw, w, abs.x, abs.y, abs.w, abs.h, p.Param),
            PipelineKind.GrayscaleInvertThreshold => GrayInvertThresholdToPng(raw, w, abs.x, abs.y, abs.w, abs.h, p.Param),
            PipelineKind.OtsuInvert => OtsuInvertToPng(raw, w, abs.x, abs.y, abs.w, abs.h),
            _ => throw new InvalidOperationException()
        };
        var res = await engine.RecognizeAsync(png);
        if (p.Kind == PipelineKind.RawConfFiltered)
            res = res.Where(x => x.Confidence >= p.ConfMin).ToList();
        var text = string.Join("", res.Select(x => x.Text));
        var conf = res.Count > 0 ? res.Average(x => x.Confidence) : 0.0;
        return (text, res.Count, conf);
    }

    private static byte[] CropRawToPng(byte[] raw, int fullW, int x, int y, int cw, int ch)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
            Array.Copy(raw, (y + row) * fullW * 4 + x * 4, buf, row * cw * 4, cw * 4);
        return EncodePng(buf, cw, ch);
    }

    private static byte[] WhiteBinarizeToPng(byte[] raw, int fullW, int x, int y, int cw, int ch, int threshold)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * cw * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = raw[srcOff + col * 4], G = raw[srcOff + col * 4 + 1], R = raw[srcOff + col * 4 + 2];
                byte v = Math.Min(B, Math.Min(G, R)) >= threshold ? (byte)0x00 : (byte)0xFF;
                buf[dstOff + col * 4] = v; buf[dstOff + col * 4 + 1] = v; buf[dstOff + col * 4 + 2] = v;
                buf[dstOff + col * 4 + 3] = 0xFF;
            }
        }
        return EncodePng(buf, cw, ch);
    }

    /// <summary>灰度 = 0.299R + 0.587G + 0.114B，≥ threshold 判白字 → 输出黑；否则输出白背景。</summary>
    private static byte[] GrayInvertThresholdToPng(byte[] raw, int fullW, int x, int y, int cw, int ch, int threshold)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * cw * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = raw[srcOff + col * 4], G = raw[srcOff + col * 4 + 1], R = raw[srcOff + col * 4 + 2];
                int gray = (299 * R + 587 * G + 114 * B) / 1000;
                byte v = gray >= threshold ? (byte)0x00 : (byte)0xFF;
                buf[dstOff + col * 4] = v; buf[dstOff + col * 4 + 1] = v; buf[dstOff + col * 4 + 2] = v;
                buf[dstOff + col * 4 + 3] = 0xFF;
            }
        }
        return EncodePng(buf, cw, ch);
    }

    /// <summary>先算灰度直方图 Otsu 阈值，再按该阈值反色输出（≥阈值→黑，否则白）。</summary>
    private static byte[] OtsuInvertToPng(byte[] raw, int fullW, int x, int y, int cw, int ch)
    {
        var gray = new byte[cw * ch];
        int[] hist = new int[256];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int gOff = row * cw;
            for (int col = 0; col < cw; col++)
            {
                byte B = raw[srcOff + col * 4], G = raw[srcOff + col * 4 + 1], R = raw[srcOff + col * 4 + 2];
                byte g = (byte)((299 * R + 587 * G + 114 * B) / 1000);
                gray[gOff + col] = g;
                hist[g]++;
            }
        }
        // Otsu
        int total = cw * ch;
        double sum = 0;
        for (int t = 0; t < 256; t++) sum += t * hist[t];
        double sumB = 0; int wB = 0; double maxVar = -1; int bestT = 128;
        for (int t = 0; t < 256; t++)
        {
            wB += hist[t]; if (wB == 0) continue;
            int wF = total - wB; if (wF == 0) break;
            sumB += t * hist[t];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            double v = (double)wB * wF * (mB - mF) * (mB - mF);
            if (v > maxVar) { maxVar = v; bestT = t; }
        }
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int gOff = row * cw;
            int dstOff = row * cw * 4;
            for (int col = 0; col < cw; col++)
            {
                byte v = gray[gOff + col] >= bestT ? (byte)0x00 : (byte)0xFF;
                buf[dstOff + col * 4] = v; buf[dstOff + col * 4 + 1] = v; buf[dstOff + col * 4 + 2] = v;
                buf[dstOff + col * 4 + 3] = 0xFF;
            }
        }
        return EncodePng(buf, cw, ch);
    }

    private static byte[] EncodePng(byte[] bgra, int cw, int ch)
    {
        var info = new SKImageInfo(cw, ch, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var sk = new SKBitmap();
        var h = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            sk.InstallPixels(info, h.AddrOfPinnedObject(), cw * 4);
            using var img = SKImage.FromBitmap(sk);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally { h.Free(); }
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
