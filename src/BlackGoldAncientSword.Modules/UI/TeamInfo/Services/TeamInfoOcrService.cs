using System.Buffers.Binary;
using System.Diagnostics;
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

        // 三 region 拼一张大图,只压一次 OCR:
        //   旧:3 次 IPC + 3 次 detector 启动 ≈ 3×~6 ms = ~18 ms
        //   新:1 次 IPC + 1 次 detector 启动 + 按 box.X 分桶 ≈ ~10 ms
        // detector 把 region 之间的纯白 spacer 当背景,自然切成 3 个独立 box;
        // 单 region 内即便切成多 box(罕见)也按 X 落入同一桶,正确。
        var stitched = StitchRegionsForOcr(rawBgra, fullWidth, fullHeight, TeamRegions);
        if (stitched.bmp == null) return Array.Empty<string>();

        ct.ThrowIfCancellationRequested();

        List<OcrResult> results;
        try
        {
            results = await _ocr.RecognizeAsync(stitched.bmp).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(TeamInfoOcrService)}] OCR error (stitched): {ex.Message}");
            return Array.Empty<string>();
        }

        // 按 box.TopLeft.X 落入哪个 region 的 [start, end) 区间,聚合到该桶。
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

    /// <summary>
    /// 把多个 region 横向拼接成一张大反色 BMP,region 之间夹 <see cref="StitchSpacerPx"/>
    /// 像素白色空隙让 detector 自动切成独立 box。返回 BMP 字节 + 每个 region 在拼图中的 X 范围。
    /// </summary>
    private const int StitchSpacerPx = 80;

    internal static (byte[]? bmp, (int xStart, int xEnd)[] regionXRanges) StitchRegionsForOcr(
        byte[] rawBgra, int fullWidth, int fullHeight, OcrRegion[] regions)
    {
        // 1. 先计算每个 region 的裁剪框 + 收集总宽 / 最大高。
        var crops = new (int x, int y, int w, int h)[regions.Length];
        int totalW = 0, maxH = 0;
        int validCount = 0;
        for (int i = 0; i < regions.Length; i++)
        {
            var r = regions[i];
            int cx = Math.Max(0, (int)(r.X * fullWidth));
            int cy = Math.Max(0, (int)(r.Y * fullHeight));
            int cw = Math.Min((int)(r.Width * fullWidth), fullWidth - cx);
            int ch = Math.Min((int)(r.Height * fullHeight), fullHeight - cy);
            if (cw <= 0 || ch <= 0) { crops[i] = (0, 0, 0, 0); continue; }
            crops[i] = (cx, cy, cw, ch);
            totalW += cw;
            if (ch > maxH) maxH = ch;
            validCount++;
        }
        if (validCount == 0 || totalW <= 0 || maxH <= 0)
            return (null, Array.Empty<(int, int)>());

        // 2. 总宽 = 各 region 宽度之和 + (有效 region 间隙数)×spacer。
        totalW += (validCount - 1) * StitchSpacerPx;

        int pixelBytes = totalW * maxH * 4;
        var bmp = new byte[BmpHeaderSize + pixelBytes];
        WriteBmpHeader(bmp, totalW, maxH);
        // 3. 整张像素区初始化为反色后的"白底"(0xFF, 0xFF, 0xFF, 0xFF)。
        //    用 Span.Fill(0xFF) 一次性填,然后再覆盖各 region 数据。
        bmp.AsSpan(BmpHeaderSize).Fill(0xFF);

        // 4. 依次把每个 region 反色后写到大图对应 X 偏移处(垂直顶对齐)。
        var ranges = new (int xStart, int xEnd)[regions.Length];
        int dstX = 0;
        for (int i = 0; i < regions.Length; i++)
        {
            var (cx, cy, cw, ch) = crops[i];
            if (cw <= 0 || ch <= 0)
            {
                ranges[i] = (-1, -1);
                continue;
            }
            BlitInvertedRegion(rawBgra, fullWidth, cx, cy, cw, ch,
                bmp, totalW, dstX, 0);
            ranges[i] = (dstX, dstX + cw);
            dstX += cw + StitchSpacerPx;
        }

        return (bmp, ranges);
    }

    /// <summary>把 rawBgra 中 [cx,cy,cw,ch] 区域反色后拷贝到目标 BMP 像素区的 (dstX, dstY) 位置。</summary>
    private static void BlitInvertedRegion(
        byte[] rawBgra, int srcFullWidth, int cx, int cy, int cw, int ch,
        byte[] dstBmp, int dstFullWidth, int dstX, int dstY)
    {
        int srcStride = srcFullWidth * 4;
        int dstStride = dstFullWidth * 4;
        for (int row = 0; row < ch; row++)
        {
            int srcOffset = (cy + row) * srcStride + cx * 4;
            int dstOffset = BmpHeaderSize + (dstY + row) * dstStride + dstX * 4;
            for (int x = 0; x < cw; x++)
            {
                int s = srcOffset + x * 4;
                int d = dstOffset + x * 4;
                dstBmp[d]     = (byte)(255 - rawBgra[s]);
                dstBmp[d + 1] = (byte)(255 - rawBgra[s + 1]);
                dstBmp[d + 2] = (byte)(255 - rawBgra[s + 2]);
                dstBmp[d + 3] = 0xFF;
            }
        }
    }

    /// <summary>写 32bpp top-down BMP 文件头(供拼图复用)。</summary>
    private static void WriteBmpHeader(byte[] bmp, int width, int height)
    {
        var span = bmp.AsSpan();
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(2, 4), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(10, 4), BmpHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(22, 4), -height);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(28, 2), 32);
    }

    /// <summary>
    /// 裁剪指定区域、反色并编码为 32bpp BMP 字节流，供 OCR 使用。
    /// <para>
    /// 旧实现走 System.Drawing.Bitmap + PNG (DEFLATE 压缩)，每帧三次 PNG 编码占用十几 ms × 3；
    /// PaddleOCR-json 内部用 OpenCV imdecode 通吃 BMP/JPG/PNG，BMP 仅 54 字节头 + 原始像素 dump，
    /// 编码开销近乎为零，故在热路径上换成 BMP。
    /// </para>
    /// <para>
    /// 同时合并裁剪/反色为一次扫描：旧实现裁剪 → 临时 byte[] 反色 → LockBits/Marshal.Copy → Bitmap.Save 共三次大块拷贝，
    /// 新实现单次循环直接把反色后的像素写入 BMP 像素区。
    /// </para>
    /// <para>
    /// 旧实现额外存在 alpha 反色 bug：0xFF → 0x00 把不透明像素反成全透明，仅靠 OpenCV 默认按 BGR 解码丢弃 alpha 才"碰巧能工作"。
    /// 新实现仅反色 BGR 三个通道，alpha 强制 0xFF。
    /// </para>
    /// </summary>
    public static (byte[]? imageBytes, int cropW, int cropH) CropAndInvert(
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

        return (EncodeInvertedBmp(rawBgra, fullWidth, cropX, cropY, cropW, cropH), cropW, cropH);
    }

    // 32bpp BGRA BMP：BITMAPFILEHEADER (14B) + BITMAPINFOHEADER (40B) + 像素数据。
    private const int BmpHeaderSize = 54;

    /// <summary>
    /// 直接拼装 32bpp top-down BMP 字节流。BITMAPINFOHEADER.biHeight 写负值 = 像素自上而下存储，
    /// 与源 rawBgra 顺序一致，无需翻转行。
    /// 反色仅作用于 BGR 三通道,alpha 强制 0xFF (旧实现把 alpha 也反色会全透明)。
    /// </summary>
    private static byte[] EncodeInvertedBmp(
        byte[] rawBgra, int fullWidth, int cropX, int cropY, int cropW, int cropH)
    {
        var bmp = new byte[BmpHeaderSize + cropW * cropH * 4];
        WriteBmpHeader(bmp, cropW, cropH);
        BlitInvertedRegion(rawBgra, fullWidth, cropX, cropY, cropW, cropH, bmp, cropW, 0, 0);
        return bmp;
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
