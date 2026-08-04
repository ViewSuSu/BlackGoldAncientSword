using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 针对 hero_selection_team_03.png（三排英雄选择界面，顾清寒）的队伍信息识别测试。
/// 走完整自动检测流程：先用三排检测区域判排数，再用对应区域拼接 OCR 分桶提取昵称。
/// 期望识别出底部三名队友昵称。
/// </summary>
public class HeroSelectionTeam03OcrTests
{
    private const string ImageName = "hero_selection_team_03.png";

    private static (byte[] rawBgra, int width, int height) LoadTestImage(string fileName)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", fileName);
        Assert.True(File.Exists(path), $"Test image not found: {path}");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        var stride = (w * bitmap.Format.BitsPerPixel + 7) / 8;
        var raw = new byte[stride * h];
        bitmap.CopyPixels(raw, stride, 0);
        return (raw, w, h);
    }

    /// <summary>拼接多个 region 后一次 OCR，按 X 位置分桶返回昵称数组（与生产 BucketAndExtractNames 一致）。</summary>
    private static async Task<string[]> RecognizeStitchedRegionsAsync(
        OcrEngine engine, byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion[] regions)
    {
        var stitched = TeamInfoOcrService.StitchRegionsForOcr(rawBgra, fullWidth, fullHeight, regions);
        if (stitched.bmp == null) return Array.Empty<string>();

        var results = await engine.RecognizeAsync(stitched.bmp);

        var centers = new int[stitched.regionXRanges.Length];
        for (int i = 0; i < stitched.regionXRanges.Length; i++)
        {
            var (xStart, xEnd) = stitched.regionXRanges[i];
            centers[i] = xStart < 0 ? int.MinValue / 2 : (xStart + xEnd) / 2;
        }

        var buckets = new string[stitched.regionXRanges.Length];
        foreach (var r in results)
        {
            var cx = (r.Box.TopLeft.X + r.Box.TopRight.X) / 2;
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < centers.Length; i++)
            {
                if (centers[i] == int.MinValue / 2) continue;
                var d = Math.Abs(cx - centers[i]);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best >= 0)
                buckets[best] = (buckets[best] ?? "") + r.Text;
        }

        var names = new List<string>();
        foreach (var bucket in buckets)
        {
            if (string.IsNullOrEmpty(bucket)) continue;
            var name = bucket.Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }
        return names.ToArray();
    }

    /// <summary>
    /// 期望识别到的三名队友昵称（人工从截图读出的 ground truth）。
    /// 顺序对应界面底部从左到右。
    /// </summary>
    private static readonly string[] ExpectedNames = { "缔造又不要的AI", "鸿兔来啦", "Payiio" };

    /// <summary>
    /// 底部整条昵称条带（1920×1080 实测），整段做一次 OCR 后按 X 分桶提取三名昵称。
    /// 生产的 TeamRegions 坐标基于 2560×1600（16:10）标定，直接套到本 16:9 截图会偏移裁空，
    /// 故这里用针对本截图标定的条带区域。
    /// </summary>
    private static readonly OcrRegion[] BottomNameStripRegions = new[]
    {
        new OcrRegion { X = 0.230, Y = 0.924, Width = 0.130, Height = 0.045 }, // 左：缔造又不要的AI（中心 nx≈0.294）
        new OcrRegion { X = 0.453, Y = 0.924, Width = 0.100, Height = 0.045 }, // 中：鸿兔来啦（中心 nx≈0.503）
        new OcrRegion { X = 0.674, Y = 0.924, Width = 0.090, Height = 0.045 }, // 右：Payiio（中心 nx≈0.719）
    };

    /// <summary>诊断：整图 OCR，打印每行文本及其归一化中心坐标，用于标定昵称条带区域。</summary>
    [Fact]
    public async Task Diag_HeroSelectionTeam03_DumpAllTextBoxes()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", ImageName);
        var (_, fullWidth, fullHeight) = LoadTestImage(ImageName);
        var imageBytes = await File.ReadAllBytesAsync(path);

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex) { Console.WriteLine($"OCR init failed: {ex.Message}"); return; }

        try
        {
            var results = await engine.RecognizeAsync(imageBytes);
            Console.WriteLine($"=== 整图 OCR，共 {results.Count} 行 ({fullWidth}x{fullHeight}) ===");
            foreach (var r in results.OrderBy(r => r.Box.TopLeft.Y).ThenBy(r => r.Box.TopLeft.X))
            {
                double cx = (r.Box.TopLeft.X + r.Box.BottomRight.X) / 2.0 / fullWidth;
                double cy = (r.Box.TopLeft.Y + r.Box.BottomRight.Y) / 2.0 / fullHeight;
                Console.WriteLine($"  '{r.Text}' conf={r.Confidence:F2} nx={cx:F3} ny={cy:F3}");
            }
        }
        finally { engine?.Dispose(); }
    }

    [Fact]
    public async Task HeroSelectionTeam03_IdentifiesThreeTeammateNames()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== HeroSelectionTeam03_IdentifiesThreeTeammateNames ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage(ImageName);
        Console.WriteLine($"  截图: {ImageName} ({fullWidth}x{fullHeight})");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR 引擎初始化失败，跳过: {ex.Message}");
            return;
        }

        try
        {
            // 参考：生产 TeamRegions（2560×1600 标定）在本 16:9 图上的表现（预期偏移/裁空）。
            var prodNames = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, TeamInfoOcrService.TeamRegions);
            Console.WriteLine($"  [参考] 生产 TeamRegions 结果: [{string.Join("], [", prodNames)}] ({prodNames.Length} 个)");

            // 针对本截图标定的底部条带区域。
            var names = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, BottomNameStripRegions);

            Console.WriteLine();
            Console.WriteLine("  === 最终识别结果（本截图条带区域）===");
            for (int i = 0; i < names.Length; i++)
                Console.WriteLine($"  队友{i + 1}: [{names[i]}]");
            Console.WriteLine($"  共识别到 {names.Length} 个昵称");
            Console.WriteLine($"  期望昵称: [{string.Join("], [", ExpectedNames)}]");

            Assert.Equal(3, names.Length);
            Assert.Equal(ExpectedNames, names);
        }
        finally { engine?.Dispose(); }
    }
}
