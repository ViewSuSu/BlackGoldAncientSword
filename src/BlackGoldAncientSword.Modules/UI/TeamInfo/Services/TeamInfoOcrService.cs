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
    /// 左侧区域已收窄宽度以避免右侧 UI 干扰。
    /// </summary>
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.253, Y = 0.878, Width = 0.088, Height = 0.040 },  // 左侧
        new OcrRegion { X = 0.456, Y = 0.876, Width = 0.113, Height = 0.043 },  // 中间
        new OcrRegion { X = 0.609, Y = 0.875, Width = 0.174, Height = 0.044 },  // 右侧
    };

    public TeamInfoOcrService(IScreenCaptureService screenCapture, IOcrService ocr)
    {
        _screenCapture = screenCapture;
        _ocr = ocr;
    }

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

        var names = new List<string>();
        foreach (var region in TeamRegions)
        {
            ct.ThrowIfCancellationRequested();

            var (pngBytes, _, _) = CropAndInvert(rawBgra, fullWidth, fullHeight, region);
            if (pngBytes == null) continue;

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
        }

        return names.Distinct().ToArray();
    }

    /// <summary>
    /// 从原始 BGRA 像素中裁剪指定区域，反色后编码为 PNG。
    /// 返回 PNG 字节数组及裁剪区域的宽高。
    /// </summary>
    public static (byte[]? pngBytes, int cropW, int cropH) CropAndInvert(
        byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion region)
    {
        var cropX = (int)(region.X * fullWidth);
        var cropY = (int)(region.Y * fullHeight);
        var cropW = (int)(region.Width * fullWidth);
        var cropH = (int)(region.Height * fullHeight);

        // 上下各扩展一个字符高度，给 OCR 引擎足够上下文
        var padY = cropH;
        cropY = Math.Max(0, cropY - padY);
        cropH = Math.Min(cropH + 2 * padY, fullHeight - cropY);

        cropX = Math.Max(0, cropX);
        cropW = Math.Min(cropW, fullWidth - cropX);

        if (cropW <= 0 || cropH <= 0) return (null, 0, 0);

        // 裁剪
        var cropped = new byte[cropW * cropH * 4];
        int srcStride = fullWidth * 4;
        int dstStride = cropW * 4;
        for (int row = 0; row < cropH; row++)
            Array.Copy(rawBgra, (cropY + row) * srcStride + cropX * 4,
                       cropped, row * dstStride, dstStride);

        // 反色：白字暗底 → 黑字白底
        for (int i = 0; i < cropped.Length; i++)
            cropped[i] = (byte)(255 - cropped[i]);

        // 编码为 PNG
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

