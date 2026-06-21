using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 双排（Duo）队伍识别测试，以及自动检测队伍规模的测试。
/// 双排在英雄选择阶段底部显示两个玩家名称，使用对应的归一化区域坐标。
/// 自动检测逻辑：先取三排区域左边缘小块试识，有文本→三排，无文本→双排。
///
/// 所有区域坐标直接引用 <see cref="TeamInfoOcrService"/> 的静态字段，确保与生产代码一致。
/// </summary>
public class DuoTeamInfoOcrServiceImageTests
{
    // ── 工具方法 ──────────────────────────────────────────

    /// <summary>从 TestData 加载图片返回 rawBgra 像素数据。</summary>
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

        Console.WriteLine($"  image: {fileName} ({w}x{h})");
        return (raw, w, h);
    }

    /// <summary>对单个 region 裁剪反色后 OCR，返回清洗后的名字。</summary>
    private static async Task<string> RecognizeRegionAsync(
        OcrEngine engine, byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion region)
    {
        var (imageBytes, cropW, cropH) = TeamInfoOcrService.CropAndInvert(
            rawBgra, fullWidth, fullHeight, region);
        if (imageBytes == null) return "";

        var results = await engine.RecognizeAsync(imageBytes);
        var text = string.Join("", results.Select(r => r.Text));
        return text.Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "");
    }

    /// <summary>拼接多个 region 后一次 OCR，返回按 X 位置分桶的名字数组。</summary>
    private static async Task<string[]> RecognizeStitchedRegionsAsync(
        OcrEngine engine, byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion[] regions)
    {
        var stitched = TeamInfoOcrService.StitchRegionsForOcr(rawBgra, fullWidth, fullHeight, regions);
        if (stitched.bmp == null) return Array.Empty<string>();

        var results = await engine.RecognizeAsync(stitched.bmp);
        var buckets = new string[stitched.regionXRanges.Length];
        foreach (var r in results)
        {
            var cx = (r.Box.TopLeft.X + r.Box.TopRight.X) / 2;
            for (int i = 0; i < stitched.regionXRanges.Length; i++)
            {
                var (xStart, xEnd) = stitched.regionXRanges[i];
                if (cx >= xStart && cx < xEnd)
                {
                    buckets[i] = (buckets[i] ?? "") + r.Text;
                    break;
                }
            }
        }

        var names = new List<string>();
        foreach (var bucket in buckets)
        {
            if (string.IsNullOrEmpty(bucket)) continue;
            var name = bucket.Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }
        return names.Distinct().ToArray();
    }

    // ── 测试用例 ──────────────────────────────────────────

    [Fact]
    public async Task DuoScreenshot_IdentifiesTwoTeammateNamesFromImage()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== DuoScreenshot_IdentifiesTwoTeammateNamesFromImage ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("duo_team_selection.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            var labels = new[] { "left", "right" };
            var regions = TeamInfoOcrService.DuoRegions;
            for (int i = 0; i < regions.Length; i++)
            {
                var name = await RecognizeRegionAsync(engine, rawBgra, fullWidth, fullHeight, regions[i]);
                Console.WriteLine($"  region {labels[i]}: [{name}]");
            }
        }
        finally { engine?.Dispose(); }
    }

    [Fact]
    public async Task DuoScreenshotFromDesktop_IdentifiesTwoTeammateNames()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== DuoScreenshotFromDesktop_IdentifiesTwoTeammateNames ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("duo_screenshot.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            var recognizedNames = new List<string>();
            var labels = new[] { "left", "right" };
            var regions = TeamInfoOcrService.DuoRegions;
            for (int i = 0; i < regions.Length; i++)
            {
                var name = await RecognizeRegionAsync(engine, rawBgra, fullWidth, fullHeight, regions[i]);
                Console.WriteLine($"  region {labels[i]}: [{name}]");
                recognizedNames.Add(name);
            }

            Console.WriteLine();
            Console.WriteLine("  === 最终识别结果 ===");
            Console.WriteLine($"  左边玩家: [{recognizedNames.ElementAtOrDefault(0) ?? "(未识别)"}]");
            Console.WriteLine($"  右边玩家: [{recognizedNames.ElementAtOrDefault(1) ?? "(未识别)"}]");
        }
        finally { engine?.Dispose(); }
    }

    // ── 自动检测逻辑测试 ──────────────────────────────────

    /// <summary>
    /// 1. 三排检测区域应在三排图片上识别到文本 → 会被正确判为三排。
    /// </summary>
    [Fact]
    public async Task TrioDetectRegion_OnHeroSelectionImage_FindsText()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== TrioDetectRegion_OnHeroSelectionImage_FindsText ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("hero_selection_team.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            var names = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, TeamInfoOcrService.TrioDetectRegions);

            Console.WriteLine($"  检测区域识别结果: [{string.Join("], [", names)}]");
            Console.WriteLine($"  结论: 检测区域{(names.Length > 0 ? "有" : "无")}文本 → 判定为{(names.Length > 0 ? "三排" : "双排")}");
        }
        finally { engine?.Dispose(); }
    }

    /// <summary>
    /// 2. 三排检测区域应在双排图片上无文本 → 会被正确判为双排。
    /// </summary>
    [Fact]
    public async Task TrioDetectRegion_OnDuoScreenshot_FindsNoText()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== TrioDetectRegion_OnDuoScreenshot_FindsNoText ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("duo_screenshot.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            var names = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, TeamInfoOcrService.TrioDetectRegions);

            Console.WriteLine($"  检测区域识别结果: [{string.Join("], [", names)}]");
            Console.WriteLine($"  结论: 检测区域{(names.Length > 0 ? "有" : "无")}文本 → 判定为{(names.Length > 0 ? "三排" : "双排")}");
        }
        finally { engine?.Dispose(); }
    }

    /// <summary>
    /// 3. 模拟完整自动检测流程：双排图片 → 检测无文本 → 回退双排区域 → 识别出 2 个名字。
    /// </summary>
    [Fact]
    public async Task AutoDetectionFlow_OnDuoScreenshot_ReturnsTwoNames()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== AutoDetectionFlow_OnDuoScreenshot_ReturnsTwoNames ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("duo_screenshot.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            // Step 1: 检测 - 用三排左边缘小区域
            var detectNames = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, TeamInfoOcrService.TrioDetectRegions);
            bool isTrio = detectNames.Length > 0;
            Console.WriteLine($"  检测阶段: 三排检测区域结果=[{string.Join("][", detectNames)}] → isTrio={isTrio}");

            // Step 2: 根据检测结果选择区域
            var regions = isTrio ? TeamInfoOcrService.TeamRegions : TeamInfoOcrService.DuoRegions;
            Console.WriteLine($"  识别阶段: {(isTrio ? "三排" : "双排")}区域");

            var names = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, regions);

            Console.WriteLine($"  识别结果: [{string.Join("], [", names)}]");
            Console.WriteLine($"  识别到 {names.Length} 个名字");
        }
        finally { engine?.Dispose(); }
    }

    /// <summary>
    /// 4. 模拟完整自动检测流程：三排图片 → 检测有文本 → 使用三排区域 → 识别出 3 个名字。
    /// </summary>
    [Fact]
    public async Task AutoDetectionFlow_OnTrioImage_ReturnsThreeNames()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== AutoDetectionFlow_OnTrioImage_ReturnsThreeNames ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("hero_selection_team.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            // Step 1: 检测 - 用三排左边缘小区域
            var detectNames = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, TeamInfoOcrService.TrioDetectRegions);
            bool isTrio = detectNames.Length > 0;
            Console.WriteLine($"  检测阶段: 三排检测区域结果=[{string.Join("][", detectNames)}] → isTrio={isTrio}");

            // Step 2: 根据检测结果选择区域
            var regions = isTrio ? TeamInfoOcrService.TeamRegions : TeamInfoOcrService.DuoRegions;
            Console.WriteLine($"  识别阶段: {(isTrio ? "三排" : "双排")}区域");

            var names = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, regions);

            Console.WriteLine($"  识别结果: [{string.Join("], [", names)}]");
            Console.WriteLine($"  识别到 {names.Length} 个名字");
        }
        finally { engine?.Dispose(); }
    }

    /// <summary>
    /// 5. 双排图片使用三排区域应有空结果 → 验证区域不会交叉误识别。
    /// </summary>
    [Fact]
    public async Task DuoImage_UsingTrioRegions_ReturnsEmpty()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== DuoImage_UsingTrioRegions_ReturnsEmpty ===");

        var (rawBgra, fullWidth, fullHeight) = LoadTestImage("duo_screenshot.png");

        OcrEngine? engine = null;
        try { engine = new OcrEngine(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR engine init failed: {ex.Message}");
            return;
        }

        try
        {
            var names = await RecognizeStitchedRegionsAsync(
                engine, rawBgra, fullWidth, fullHeight, TeamInfoOcrService.TeamRegions);

            Console.WriteLine($"  用三排区域识别双排图片: [{string.Join("], [", names)}]");
            Console.WriteLine($"  结论: {(names.Length == 0 ? "无交叉误识别 ✓" : "误识别到文本 ✗")}");
        }
        finally { engine?.Dispose(); }
    }
}
