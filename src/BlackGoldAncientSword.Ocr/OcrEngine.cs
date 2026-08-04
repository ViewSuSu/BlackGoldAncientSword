using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using RapidOcrNet;
using SkiaSharp;

namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// PP-OCRv5 OCR 引擎封装 (基于 RapidOcrNet,进程内 ONNX Runtime 推理)。
/// <list type="bullet">
///   <item>三段推理 det → cls → rec 全部跑在 ONNX Runtime 进程内,无子进程 / 无 stdin/stdout IPC。</item>
///   <item>单例 + <see cref="SemaphoreSlim"/> 串行化保护 RapidOcr 内部状态。</item>
///   <item>模型 + 字典自首次 <see cref="RecognizeAsync"/> 或 <see cref="PrewarmAsync"/> 时加载,
///         之后驻留内存,后续每次识别仅跑推理。</item>
/// </list>
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class OcrEngine : IOcrService, IDisposable
{
    private readonly string _modelDir;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RapidOcr _ocr = new();

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// 默认推理选项。<c>DoAngle=false</c>:游戏昵称都是横排,跳过 cls 节省一段推理。
    /// <c>TextScore=0.3</c>:保留低置信结果,业务侧再过滤,避免误丢小字号昵称。
    /// </summary>
    private static readonly RapidOcrOptions DefaultOptions = RapidOcrOptions.Default with
    {
        DoAngle = false,
        TextScore = 0.3f,
    };

    /// <param name="modelDir">PP-OCRv5 模型目录,为空则自动查找。</param>
    public OcrEngine(string? modelDir = null)
    {
        _modelDir = modelDir ?? ResolveModelDir();
    }

    /// <inheritdoc />
    public async Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return new List<OcrResult>();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureInitialized();

            using var bmp = SKBitmap.Decode(imageBytes);
            if (bmp == null)
                throw new InvalidOperationException("SKBitmap.Decode 失败,图片字节非法或编码不支持");

            var result = _ocr.Detect(bmp, DefaultOptions);
            return ConvertResults(result);
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(byte[] imageBytes)
    {
        var results = await RecognizeAsync(imageBytes).ConfigureAwait(false);
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 加载 4 个 ONNX 模型 + 字典约 200~500 ms。再跑一次最小推理触发 ONNX session
    /// 内部 buffer 与 kernel 编译,把首次推理冷启动成本摊到 App 启动阶段。
    /// </remarks>
    public async Task PrewarmAsync(CancellationToken ct = default)
    {
        Debug.WriteLine($"[{nameof(OcrEngine)}] 预热开始,模型目录={_modelDir}");
        var totalSw = Stopwatch.StartNew();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                Debug.WriteLine($"[{nameof(OcrEngine)}] 预热跳过 (模型已加载)");
                return;
            }

            var loadSw = Stopwatch.StartNew();
            EnsureInitialized();
            loadSw.Stop();
            Debug.WriteLine($"[{nameof(OcrEngine)}] 模型加载完成 ({loadSw.ElapsedMilliseconds} ms)");

            // 8×8 白底,跑一遍 det/rec 触发 ONNX 内部初始化。
            var inferSw = Stopwatch.StartNew();
            using var bmp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.Erase(SKColors.White);
            _ = _ocr.Detect(bmp, DefaultOptions);
            inferSw.Stop();
            Debug.WriteLine($"[{nameof(OcrEngine)}] 首次推理完成 ({inferSw.ElapsedMilliseconds} ms),预热总耗时 {totalSw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, nameof(OcrEngine), "预热失败");
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>
    /// 加载 4 个模型 + 字典。调用方必须已持有 <see cref="_gate"/>。
    /// 失败抛异常,下次调用重试。
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;

        var det = Path.Combine(_modelDir, "ch_PP-OCRv5_det_mobile.onnx");
        var cls = Path.Combine(_modelDir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
        var rec = Path.Combine(_modelDir, "ch_PP-OCRv5_rec_mobile.onnx");
        var keys = Path.Combine(_modelDir, "ppocrv5_dict.txt");

        foreach (var path in new[] { det, cls, rec, keys })
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"OCR 模型缺失: {path}");
        }

        _ocr.InitModels(det, cls, rec, keys);
        _initialized = true;
    }

    /// <summary>
    /// 把 RapidOcrNet 的 <see cref="RapidOcrNet.OcrResult"/> 转成本地 <see cref="OcrResult"/> 列表。
    /// BoxPoints 顺时针存储 [TL, TR, BR, BL],与本地 <see cref="OcrBox"/> 角点一一对应。
    /// </summary>
    private static List<OcrResult> ConvertResults(RapidOcrNet.OcrResult result)
    {
        var blocks = result.TextBlocks;
        if (blocks == null || blocks.Length == 0)
            return new List<OcrResult>();

        var list = new List<OcrResult>(blocks.Length);
        foreach (var b in blocks)
        {
            if (string.IsNullOrEmpty(b.Text)) continue;

            var p = b.BoxPoints;
            double confidence = b.CharScores != null && b.CharScores.Length > 0
                ? b.CharScores.Average()
                : 0;

            list.Add(new OcrResult
            {
                Text = b.Text,
                Confidence = confidence,
                Box = new OcrBox
                {
                    TopLeft = new OcrPoint((int)p[0].X, (int)p[0].Y),
                    TopRight = new OcrPoint((int)p[1].X, (int)p[1].Y),
                    BottomRight = new OcrPoint((int)p[2].X, (int)p[2].Y),
                    BottomLeft = new OcrPoint((int)p[3].X, (int)p[3].Y),
                }
            });
        }
        return list;
    }

    /// <summary>
    /// 自动查找 PP-OCRv5 模型目录。优先 bin 输出旁的 <c>ocr_engine/models/v5/</c>,
    /// 测试场景下回退到源码树。
    /// </summary>
    private static string ResolveModelDir()
    {
        var asmDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(asmDir, "ocr_engine", "models", "v5"),
            Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "ocr_engine", "models", "v5")),
            Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "ocr_engine", "models", "v5")),
            Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "ocr_engine", "models", "v5")),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "ch_PP-OCRv5_det_mobile.onnx")))
                return c;
        }

        throw new InvalidOperationException(
            "未找到 PP-OCRv5 模型目录。请确保 src/ocr_engine/models/v5/ 含:\n" +
            "  - ch_PP-OCRv5_det_mobile.onnx\n" +
            "  - ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx\n" +
            "  - ch_PP-OCRv5_rec_mobile.onnx\n" +
            "  - ppocrv5_dict.txt\n" +
            "下载源: https://www.modelscope.cn/models/RapidAI/RapidOCR");
    }

    /// <summary>
    /// Dispose 与请求竞态时容忍 _gate 已被释放的情况。
    /// </summary>
    private void ReleaseGate()
    {
        if (_disposed) return;
        try { _gate.Release(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _ocr.Dispose(); }
        catch (Exception ex)
        {
            AppLog.Error(ex, nameof(OcrEngine), "dispose RapidOcr failed");
        }

        try { _gate.Dispose(); }
        catch (Exception ex)
        {
            AppLog.Error(ex, nameof(OcrEngine), "dispose gate failed");
        }
    }
}

// ═══════════════════════════════════════════════
//  OCR 结果数据结构
// ═══════════════════════════════════════════════

public class OcrResult
{
    public string Text { get; set; } = "";
    public double Confidence { get; set; }
    public OcrBox Box { get; set; } = new();
}

public class OcrBox
{
    public OcrPoint TopLeft { get; set; } = new();
    public OcrPoint TopRight { get; set; } = new();
    public OcrPoint BottomRight { get; set; } = new();
    public OcrPoint BottomLeft { get; set; } = new();
}

public class OcrPoint
{
    public int X { get; set; }
    public int Y { get; set; }
    public OcrPoint() { }
    public OcrPoint(int x, int y) { X = x; Y = y; }
}
