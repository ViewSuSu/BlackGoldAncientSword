using System.Buffers.Binary;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Ocr;
using BlackGoldAncientSword.ScreenCapture;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

/// <summary>
/// 队伍信息 OCR 识别服务实现。
/// 截取游戏窗口的队友名字区域，通过 PaddleOCR 识别玩家名称。
/// 生产管线：白字二值化 (<see cref="BlitBinarizedWhiteRegion"/>)。游戏昵称字体为纯白，
/// min(B,G,R) 阈值过滤掉血条/图标/装饰等彩色像素，OCR 只面对干净黑底白字，繁简
/// 抗性优于裸色管线；对末尾 `.` `丶` 装饰点丢失依赖 <see cref="TeamMemberNameCorrector"/> 校正。
/// 裸色实现 (<see cref="BlitRawRegion"/>) 保留供离线对比测试。
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class TeamInfoOcrService : ITeamInfoOcrService
{
    private readonly IScreenCaptureService _screenCapture;
    private readonly IOcrService _ocr;

    /// <summary>
    /// 三个队友名字区域的归一化坐标（基于 2560×1600 参考分辨率, 16:10）。
    /// 校准点：红色边框像素扫描得出的红框内框。
    /// 绝对像素: L(770,1456,284x54) / M(1204,1456,288x54) / R(1650,1456,304x54)。
    /// </summary>
    internal static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },  // 左侧
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },  // 中间
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },  // 右侧
    };

    /// <summary>
    /// 双排两个队友名字区域的归一化坐标（基于 2560×1600 参考分辨率, 16:10）。
    /// 校准点：红色边框像素扫描得出的红框内框。
    /// 绝对像素: L(990,1454,282x56) / R(1428,1454,302x56)。
    /// </summary>
    internal static readonly OcrRegion[] DuoRegions = new[]
    {
        new OcrRegion { X = 0.386719, Y = 0.908750, Width = 0.110156, Height = 0.035000 },  // 左侧
        new OcrRegion { X = 0.557813, Y = 0.908750, Width = 0.117969, Height = 0.035000 },  // 右侧
    };

    /// <summary>
    /// 三排检测用小区域：trio 左 region 的最左截 + trio 右 region 的最右截。
    /// <para>
    /// 取两端外侧截而非内侧，是为避开 duo 玩家名字 X 范围：
    /// duo 左玩家 X ∈ [0.386, 0.497]，duo 右玩家 X ∈ [0.558, 0.677]。
    /// trio 检测区若落入这两段（旧实现右截 [0.646, 0.696] 即与 duo 右玩家 [0.646, 0.677] 重叠 31px），
    /// duo 画面会被误识到字符 → 误判为 trio。
    /// </para>
    /// <para>
    /// trio 与 duo 的 Y 范围实际重叠约 38px（注释中所谓"相差 34px"是错的），无法靠 Y 区分，
    /// 必须靠 X 完全避开 duo 玩家 X 范围。
    /// </para>
    /// <para>
    /// (2560×1600 校准后) trio L X=[0.301,0.412], duo L X=[0.387,0.497], trio R X=[0.645,0.763], duo R X=[0.558,0.676]。
    /// 左截 X=[0.301, 0.371]：在 duo 左玩家 0.387 之前留 ~41px buffer，安全。
    /// 右截 X=[0.693, 0.763]：在 duo 右玩家 0.676 之后留 ~43px buffer，安全。
    /// </para>
    /// </summary>
    internal static readonly OcrRegion[] TrioDetectRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.070, Height = 0.033750 }, // trio 左 region 最左截
        new OcrRegion { X = 0.693000, Y = 0.910000, Width = 0.070, Height = 0.033750 }, // trio 右 region 最右截
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

        return BucketAndExtractNames(stitched.regionXRanges, results);
    }

    public async Task<string[]> RecognizeDuoTeamMembersAsync(CancellationToken ct = default)
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

        // 双排 region 拼接逻辑与三排完全一致,仅 regions 数据和 region 数量不同。
        // 复用 StitchRegionsForOcr + 按 X 分桶 pipeline。
        var stitched = StitchRegionsForOcr(rawBgra, fullWidth, fullHeight, DuoRegions);
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
            Debug.WriteLine($"[{nameof(TeamInfoOcrService)}] Duo OCR error (stitched): {ex.Message}");
            return Array.Empty<string>();
        }

        return BucketAndExtractNames(stitched.regionXRanges, results);
    }

    public async Task<string[]> RecognizeTeamMembersAutoAsync(CancellationToken ct = default)
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

        ct.ThrowIfCancellationRequested();

        // Step 1: 检测队伍规模
        // 取 trio 左 region 最左截 + trio 右 region 最右截拼成一块检测图。
        // trio 与 duo 的 Y 范围实际重叠约 38px（屏幕底栏位置接近），无法靠 Y 区分，
        // 必须靠 X 完全避开 duo 玩家 X 范围（左玩家 [0.386,0.497]、右玩家 [0.558,0.677]）。
        // 详见 TrioDetectRegions 注释。
        var detectStitched = StitchRegionsForOcr(rawBgra, fullWidth, fullHeight, TrioDetectRegions);
        bool isTrio = false;

        if (detectStitched.bmp != null)
        {
            try
            {
                var detectResults = await _ocr.RecognizeAsync(detectStitched.bmp).ConfigureAwait(false);
                isTrio = detectResults.Count > 0
                    && detectResults.Any(r => !string.IsNullOrWhiteSpace(r.Text));
            }
            catch
            {
                // 检测失败时安全地默认走双排
            }
        }

        // Step 2: 根据检测结果选择对应的完整区域进行识别
        var regions = isTrio ? TeamRegions : DuoRegions;

        var stitched = StitchRegionsForOcr(rawBgra, fullWidth, fullHeight, regions);
        if (stitched.bmp == null) return Array.Empty<string>();

        List<OcrResult> results;
        try
        {
            results = await _ocr.RecognizeAsync(stitched.bmp).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(TeamInfoOcrService)}] Auto OCR error (stitched): {ex.Message}");
            return Array.Empty<string>();
        }

        return BucketAndExtractNames(stitched.regionXRanges, results);
    }

    /// <summary>
    /// 把 OCR 结果按"最近槽位中心"分桶并清洗成名字数组。
    /// <para>
    /// 旧实现按 [xStart, xEnd) 严判分桶 + 末尾 <c>Distinct()</c> 去重，存在两个 bug：
    /// </para>
    /// <list type="number">
    /// <item>槽位之间留 80 px 白色 spacer 死区，PaddleOCR detector 给 box 加 padding 后
    /// box 中心可能落入死区被静默丢弃 → 三排只识别到两人。</item>
    /// <item><c>Distinct()</c> 把两个槽位被 OCR 误识成相同字符串的情况合并成一项 → 同样丢人。
    /// 槽位识别的语义本就是按位置区分玩家，去重无意义。</item>
    /// </list>
    /// <para>
    /// 新实现：每个 OCR box 按中心 X 与最近槽位中心距离归属，无死区；按槽位顺序输出，
    /// 同名误识保留两条以便后续 HTTP 查询失败时仍可见。
    /// </para>
    /// </summary>
    /// <summary>
    /// rec 置信度低于该阈值的检测框判为幻影丢弃。
    /// </summary>
    /// <remarks>
    /// 裸色管线下主检测置信度稳定 ≥ 0.85，幻影（如 hero_selection_team_02 M 区域中被过滤的 `-` 竖块，
    /// conf ≈ 0.63）远低于 0.7，天然分水岭。设 0.7 可清幻影且不伤合法昵称。
    /// </remarks>
    private const double MinOcrConfidence = 0.7;

    private static string[] BucketAndExtractNames(
        (int xStart, int xEnd)[] regionXRanges, List<OcrResult> results)
    {
        var centers = new int[regionXRanges.Length];
        for (int i = 0; i < regionXRanges.Length; i++)
        {
            var (xStart, xEnd) = regionXRanges[i];
            // StitchRegionsForOcr 对无效 region 写入 (-1,-1)；此处保持中心为负，
            // 任何有效 cx 都不会与之距离最近，等价于该槽位被跳过。
            centers[i] = xStart < 0 ? int.MinValue / 2 : (xStart + xEnd) / 2;
        }

        var buckets = new string[regionXRanges.Length];
        foreach (var r in results)
        {
            if (r.Confidence < MinOcrConfidence) continue;
            var cx = (r.Box.TopLeft.X + r.Box.TopRight.X) / 2;
            int best = -1;
            int bestDist = int.MaxValue;
            for (int i = 0; i < centers.Length; i++)
            {
                if (centers[i] == int.MinValue / 2) continue;
                var d = Math.Abs(cx - centers[i]);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
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
    /// 把多个 region 横向白字二值化后拼接成一张大 BMP，region 之间夹 <see cref="StitchSpacerPx"/> 像素
    /// 纯白 spacer（与二值化后白底一致）让 detector 自动切成独立 box。
    /// 返回 BMP 字节 + 每个 region 在拼图中的 X 范围。
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

        // 3. 白字二值化管线：spacer 用纯白 (0xFF, 0xFF, 0xFF, 0xFF)，与二值化后的白底一致；
        //    整张先填 0xFF。
        var pxSpan = bmp.AsSpan(BmpHeaderSize);
        pxSpan.Fill(0xFF);

        // 4. 依次把每个 region 白字二值化后拷到大图对应 X 偏移处 (垂直顶对齐)。
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
            BlitBinarizedWhiteRegion(rawBgra, fullWidth, cx, cy, cw, ch,
                bmp, totalW, dstX, 0);
            ranges[i] = (dstX, dstX + cw);
            dstX += cw + StitchSpacerPx;
        }

        return (bmp, ranges);
    }

    /// <summary>
    /// 裸色拷贝：源 [cx,cy,cw,ch] 直接搬到目标 BMP (dstX, dstY) 处，alpha 强置 0xFF。
    /// 替代 <see cref="BlitBinarizedWhiteRegion"/> 用于生产管线。
    /// 保留裸色让 OCR 拥有完整字符纹理信息，避免二值化阈值造成 `.` 装饰点吞掉 / `耍→要` 细笔画丢失。
    /// </summary>
    private static void BlitRawRegion(
        byte[] rawBgra, int srcFullWidth, int cx, int cy, int cw, int ch,
        byte[] dstBmp, int dstFullWidth, int dstX, int dstY)
    {
        int srcStride = srcFullWidth * 4;
        int dstStride = dstFullWidth * 4;
        int rowBytes = cw * 4;
        for (int row = 0; row < ch; row++)
        {
            int srcOffset = (cy + row) * srcStride + cx * 4;
            int dstOffset = BmpHeaderSize + (dstY + row) * dstStride + dstX * 4;
            Buffer.BlockCopy(rawBgra, srcOffset, dstBmp, dstOffset, rowBytes);
            for (int a = dstOffset + 3; a < dstOffset + rowBytes; a += 4)
                dstBmp[a] = 0xFF;
        }
    }

    /// <summary>游戏昵称为纯白字，min(B,G,R) ≥ 该阈值判为白字像素。</summary>
    /// <remarks>
    /// 200 是甜点值：低于 200 会把 AA 边缘 + 血条/装饰的浅色噪声吸进白字集合（引入幻影 `1ILA` 等）；
    /// 高于 220 会把 `耍` 这类细笔画像素判为背景导致 rec 认错（`耍→要`）。
    /// </remarks>
    private const int WhiteTextThreshold = 200;

    /// <summary>
    /// 白字二值化：源 [cx,cy,cw,ch] 内 min(B,G,R) ≥ <see cref="WhiteTextThreshold"/> 判为白字 → 输出黑 (0x00)；
    /// 其余像素判为背景/干扰 → 输出白 (0xFF)。等价于"只保留白字 + 反色"一步搞定。
    /// <para>
    /// 相比原始 BGR 全通道反色：非白色的血条/图标/装饰在 min-channel 阈值下未通过，
    /// 直接抹平为纯白背景，det 阶段不会把它们识别成幻影文字。
    /// alpha 通道强制 0xFF。
    /// </para>
    /// </summary>
    private static void BlitBinarizedWhiteRegion(
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
                byte b = rawBgra[s];
                byte g = rawBgra[s + 1];
                byte r = rawBgra[s + 2];
                byte v = Math.Min(b, Math.Min(g, r)) >= WhiteTextThreshold ? (byte)0x00 : (byte)0xFF;
                dstBmp[d]     = v;
                dstBmp[d + 1] = v;
                dstBmp[d + 2] = v;
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
    /// 裁剪指定区域、白字二值化并编码为 32bpp BMP 字节流，供 OCR 使用。
    /// <para>
    /// 白字二值化替代了原先的 BGR 全通道反色：昵称是纯白字，血条/图标/装饰是彩色，
    /// 通过 min-channel 阈值把非白像素抹为纯白背景，避免 det 阶段把彩色 UI 误识为幻影文字
    /// （如 `13`、`不`、`ILA` 等）。参见 <see cref="BlitBinarizedWhiteRegion"/>。
    /// </para>
    /// <para>
    /// 保留 BMP 编码：PaddleOCR / RapidOcr 内部走 OpenCV imdecode 通吃 BMP，54 字节头 + 原始像素 dump，
    /// 编码开销近乎为零。BITMAPINFOHEADER.biHeight 写负值表示 top-down，与源 rawBgra 顺序一致无需翻行。
    /// </para>
    /// </summary>
    public static (byte[]? imageBytes, int cropW, int cropH) CropAndBinarizeWhite(
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

        return (EncodeBinarizedWhiteBmp(rawBgra, fullWidth, cropX, cropY, cropW, cropH), cropW, cropH);
    }

    // 32bpp BGRA BMP：BITMAPFILEHEADER (14B) + BITMAPINFOHEADER (40B) + 像素数据。
    private const int BmpHeaderSize = 54;

    /// <summary>
    /// 拼装 32bpp top-down BMP 字节流，像素区通过白字二值化生成。
    /// </summary>
    private static byte[] EncodeBinarizedWhiteBmp(
        byte[] rawBgra, int fullWidth, int cropX, int cropY, int cropW, int cropH)
    {
        var bmp = new byte[BmpHeaderSize + cropW * cropH * 4];
        WriteBmpHeader(bmp, cropW, cropH);
        BlitBinarizedWhiteRegion(rawBgra, fullWidth, cropX, cropY, cropW, cropH, bmp, cropW, 0, 0);
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


