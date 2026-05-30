using System.Diagnostics;
using System.Windows.Media;
using System.IO;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Ocr;
using BlackGoldAncientSword.ScreenCapture;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

/// <summary>
/// 队伍信息 OCR 识别服务实现。
/// 截取游戏窗口的队友名字区域，通过 PaddleOCR 识别玩家名称。
/// 截图为白字暗底，OCR 前自动反色处理。
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class TeamInfoOcrService : ITeamInfoOcrService
{
    private readonly IScreenCaptureService _screenCapture;
    private readonly IOcrService _ocr;

    /// <summary>
    /// 三个队友名字区域的归一化坐标（基于 2048×1152 参考分辨率）。
    /// </summary>
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },  // 左侧
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },  // 中间
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },  // 右侧
    };

    public TeamInfoOcrService(IScreenCaptureService screenCapture, IOcrService ocr)
    {
        _screenCapture = screenCapture;
        _ocr = ocr;
    }

    private static string OcrTempDir => System.IO.Path.Combine(
        Framework.Services.AppSettings.GetDefaultCachePath(), "ocr");

    public async Task<string[]> RecognizeTeamMembersAsync(CancellationToken ct = default)
    {
        if (!_screenCapture.TryFindGameWindow("NarakaBladepoint", out var hwnd))
            return Array.Empty<string>();

        byte[] rawBgra;
        int fullWidth, fullHeight;
        try
        {
            rawBgra = _screenCapture.CaptureFullRaw(hwnd, out fullWidth, out fullHeight);
        }
        catch
        {
            return Array.Empty<string>();
        }

        if (fullWidth <= 0 || fullHeight <= 0) return Array.Empty<string>();

        var tempDir = OcrTempDir;
        try { System.IO.Directory.CreateDirectory(tempDir); } catch { }

        var tempFiles = new List<string>();
        var names = new List<string>();
        var regionIndex = 0;
        foreach (var region in TeamRegions)
        {
            ct.ThrowIfCancellationRequested();

            var (pngBytes, _, _) = CropAndInvert(rawBgra, fullWidth, fullHeight, region);
            if (pngBytes == null) { regionIndex++; continue; }

            // Save temp screenshot for debug / cache clear cleanup
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var tempPath = System.IO.Path.Combine(tempDir, $"ocr_r{regionIndex}_{ts}.png");
                await System.IO.File.WriteAllBytesAsync(tempPath, pngBytes, ct);
                tempFiles.Add(tempPath);
            }
            catch { }

            try
            {
                var text = await Task.Run(() => _ocr.RecognizeText(pngBytes), ct);
                var name = text.Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamInfoOcr] OCR error: {ex.Message}");
            }

            regionIndex++;
        }

        // Clean up temp files from this cycle
        foreach (var f in tempFiles)
        {
            try { System.IO.File.Delete(f); } catch { }
        }

        return names.Distinct().ToArray();
    }

    /// <summary>
    /// 裁剪指定区域并编码为 PNG（无反色），用于视觉检查。
    /// </summary>
    public static (byte[]? pngBytes, int cropW, int cropH) CropRegion(
        byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion region)
    {
        var cropX = (int)(region.X * fullWidth);
        var cropY = (int)(region.Y * fullHeight);
        var cropW = (int)(region.Width * fullWidth);
        var cropH = (int)(region.Height * fullHeight);

        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropW = Math.Min(cropW, fullWidth - cropX);
        cropH = Math.Min(cropH, fullHeight - cropY);

        if (cropW <= 0 || cropH <= 0) return (null, 0, 0);

        var cropped = new byte[cropW * cropH * 4];
        int srcStride = fullWidth * 4;
        int dstStride = cropW * 4;
        for (int row = 0; row < cropH; row++)
            Array.Copy(rawBgra, (cropY + row) * srcStride + cropX * 4,
                       cropped, row * dstStride, dstStride);

        var bitmap = BitmapSource.Create(
            cropW, cropH, 96, 96,
            PixelFormats.Bgra32, null, cropped, dstStride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), cropW, cropH);
    }

    /// <summary>
    /// 裁剪指定区域、反色并编码为 PNG，供 OCR 使用。
    /// </summary>
    public static (byte[]? pngBytes, int cropW, int cropH) CropAndInvert(
        byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion region)
    {
        var cropX = (int)(region.X * fullWidth);
        var cropY = (int)(region.Y * fullHeight);
        var cropW = (int)(region.Width * fullWidth);
        var cropH = (int)(region.Height * fullHeight);

        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropW = Math.Min(cropW, fullWidth - cropX);
        cropH = Math.Min(cropH, fullHeight - cropY);

        if (cropW <= 0 || cropH <= 0) return (null, 0, 0);

        var cropped = new byte[cropW * cropH * 4];
        int srcStride = fullWidth * 4;
        int dstStride = cropW * 4;
        for (int row = 0; row < cropH; row++)
            Array.Copy(rawBgra, (cropY + row) * srcStride + cropX * 4,
                       cropped, row * dstStride, dstStride);

        // 反色：白字暗底 → 黑字白底
        for (int i = 0; i < cropped.Length; i++)
            cropped[i] = (byte)(255 - cropped[i]);

        var bitmap = BitmapSource.Create(
            cropW, cropH, 96, 96,
            PixelFormats.Bgra32, null, cropped, dstStride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), cropW, cropH);
    }
}

/// <summary>
/// OCR 识别区域的归一化坐标定义。
/// </summary>
public class OcrRegion
{
    /// <summary>距离左边百分比 (0.0 ~ 1.0)</summary>
    public double X { get; set; }
    /// <summary>距离顶部百分比 (0.0 ~ 1.0)</summary>
    public double Y { get; set; }
    /// <summary>宽度百分比 (0.0 ~ 1.0)</summary>
    public double Width { get; set; }
    /// <summary>高度百分比 (0.0 ~ 1.0)</summary>
    public double Height { get; set; }
}
