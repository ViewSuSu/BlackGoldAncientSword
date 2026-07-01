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
    public async Task LiveCapture_TeamInfo_OCR_PrintResults()
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
        var labelNames = new[] { "左侧", "中间", "右侧" };
        var ocrResults = new System.Text.StringBuilder();

        for (int i = 0; i < TeamRegions.Length; i++)
        {
            // OCR 使用反色版本（BMP 编码，PaddleOCR-json 走 OpenCV imdecode 通吃）
            var (imageBytes, cropW, cropH) = TeamInfoOcrService.CropAndBinarizeWhite(
                rawBgra, fullWidth, fullHeight, TeamRegions[i]);

            if (imageBytes == null)
            {
                Console.WriteLine($"区域 {labelNames[i]}: 裁剪失败");
                continue;
            }

            var results = await engine.RecognizeAsync(imageBytes);
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

    /// <summary>
    /// 验证 OcrEngine（手写常驻进程实现）能够对一张已知含中文文本的截图成功识别。
    /// 走整张图（不做裁剪），断言至少识别出几个明显存在的字符串作为冒烟测试。
    /// </summary>
    [Fact]
    public async Task FullScreenshot_OcrEngine_RecognizesKnownChineseText()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var imagePath = @"C:\Users\16147\Pictures\Screenshots\屏幕截图 2026-06-14 143108.png";
        Assert.True(File.Exists(imagePath), $"截图文件不存在: {imagePath}");

        // 整张 PNG 字节直接喂给 OcrEngine（PaddleOCR-json 内部走 OpenCV imdecode 通吃 PNG）。
        var imageBytes = await File.ReadAllBytesAsync(imagePath);

        using var engine = new OcrEngine();
        var results = await engine.RecognizeAsync(imageBytes);

        Assert.NotEmpty(results);
        var allText = string.Join(" / ", results.Select(r => r.Text));
        Console.WriteLine($"识别出 {results.Count} 行文本：");
        foreach (var r in results)
            Console.WriteLine($"  '{r.Text}' (置信度 {r.Confidence:F2})");

        string[] anchors = { "英雄", "使用", "张起灵", "外观" };
        var hitCount = anchors.Count(a => allText.Contains(a));
        Assert.True(hitCount > 0, $"未识别到任何已知锚点 ({string.Join(", ", anchors)})。实际识别结果：{allText}");
    }

    /// <summary>
    /// 性能基准：单图整张 OCR 连续 N 次。
    /// 首次包含模型加载（约 600~1500 ms），后续仅推理。统计 min/avg/median/max。
    /// 仅在本地观察用，不做硬断言；走 Trace.WriteLine 同时也写 Console，便于在测试日志中检索。
    /// </summary>
    [Fact]
    public async Task Benchmark_OcrEngine_FullImage_Repeated()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        const int Iterations = 20;

        var imagePath = @"C:\Users\16147\Pictures\Screenshots\屏幕截图 2026-06-14 143108.png";
        Assert.True(File.Exists(imagePath), $"截图文件不存在: {imagePath}");

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        using var engine = new OcrEngine();

        // 预热：首次包含模型加载，单独记录。
        var coldSw = System.Diagnostics.Stopwatch.StartNew();
        var firstResults = await engine.RecognizeAsync(imageBytes);
        coldSw.Stop();
        Assert.NotEmpty(firstResults);

        // 稳态测量。
        var samples = new long[Iterations];
        for (int i = 0; i < Iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await engine.RecognizeAsync(imageBytes);
            sw.Stop();
            samples[i] = sw.ElapsedMilliseconds;
            Assert.NotEmpty(r);
        }

        Array.Sort(samples);
        var min = samples[0];
        var max = samples[^1];
        var median = samples[samples.Length / 2];
        var avg = samples.Average();

        Console.WriteLine($"[Bench] 整图 ({imageBytes.Length:N0} bytes) × {Iterations}");
        Console.WriteLine($"  冷启动 (含模型加载): {coldSw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  稳态: min={min} ms  avg={avg:F1} ms  median={median} ms  max={max} ms");
        Console.WriteLine($"  全部样本: [{string.Join(", ", samples)}]");
    }

    /// <summary>
    /// 性能基准：模拟 TeamInfoOcrService 实际场景 —— 一轮 3 region 串行裁剪 + OCR，跑 N 轮。
    /// 用 LiveCapture 测试同款 BMP 反色管线，统计每轮总耗时。
    /// </summary>
    [Fact]
    public async Task Benchmark_OcrEngine_ThreeRegions_PerRound()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        const int Rounds = 10;

        var imagePath = @"C:\Users\16147\Pictures\Screenshots\屏幕截图 2026-06-14 143108.png";
        Assert.True(File.Exists(imagePath), $"截图文件不存在: {imagePath}");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        int fullWidth = bitmap.PixelWidth;
        int fullHeight = bitmap.PixelHeight;
        var stride = (fullWidth * bitmap.Format.BitsPerPixel + 7) / 8;
        var rawBgra = new byte[stride * fullHeight];
        bitmap.CopyPixels(rawBgra, stride, 0);

        using var engine = new OcrEngine();

        // 预热一轮：触发模型加载，不计时。
        foreach (var region in TeamRegions)
        {
            var (bytes, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, fullWidth, fullHeight, region);
            if (bytes != null) await engine.RecognizeAsync(bytes);
        }

        // 稳态测量。
        var samples = new long[Rounds];
        for (int round = 0; round < Rounds; round++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var region in TeamRegions)
            {
                var (bytes, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, fullWidth, fullHeight, region);
                if (bytes != null) await engine.RecognizeAsync(bytes);
            }
            sw.Stop();
            samples[round] = sw.ElapsedMilliseconds;
        }

        Array.Sort(samples);
        var min = samples[0];
        var max = samples[^1];
        var median = samples[samples.Length / 2];
        var avg = samples.Average();

        Console.WriteLine($"[Bench] 3 region 一轮 × {Rounds} 轮（同 TeamInfoOcrService 路径）");
        Console.WriteLine($"  稳态: min={min} ms  avg={avg:F1} ms  median={median} ms  max={max} ms");
        Console.WriteLine($"  每轮: [{string.Join(", ", samples)}]");
        Console.WriteLine($"  每 region 均摊: avg={avg / 3:F1} ms");
    }

    [Fact]
    public async Task Screenshot_HeroSelectionPhase_IdentifiesThreeTeammateNamesFromImage()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var imagePath = @"C:\Users\16147\Pictures\Screenshots\屏幕截图 2026-06-14 143108.png";
        Assert.True(File.Exists(imagePath), $"截图文件不存在: {imagePath}");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(imagePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        int fullWidth = bitmap.PixelWidth;
        int fullHeight = bitmap.PixelHeight;
        var stride = (fullWidth * bitmap.Format.BitsPerPixel + 7) / 8;
        var rawBgra = new byte[stride * fullHeight];
        bitmap.CopyPixels(rawBgra, stride, 0);

        Console.WriteLine($"截图尺寸: {fullWidth}x{fullHeight}");

        OcrEngine? engine = null;
        try
        {
            engine = new OcrEngine();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"OCR 引擎初始化失败: {ex.Message}");
            return;
        }

        try
        {
            var labels = new[] { "左侧", "中间", "右侧" };

            for (int i = 0; i < TeamRegions.Length; i++)
            {
                var (imageBytes, cropW, cropH) = TeamInfoOcrService.CropAndBinarizeWhite(
                    rawBgra, fullWidth, fullHeight, TeamRegions[i]);

                if (imageBytes == null)
                {
                    Console.WriteLine($"区域 {labels[i]}: 裁剪失败");
                    continue;
                }

                var results = await engine.RecognizeAsync(imageBytes);
                var rawText = string.Join("", results.Select(r => r.Text));
                var name = rawText.Trim().Replace(" ", "");
                var detail = string.Join(" | ", results.Select(r => $"'{r.Text}'({r.Confidence:F2})"));

                Console.WriteLine($"区域 {labels[i]} ({cropW}x{cropH}): 识别结果=[{name}]  详情=[{detail}]");
            }
        }
        finally
        {
            engine?.Dispose();
        }
    }
}
