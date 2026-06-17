using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// PaddleOCR 引擎封装。
/// 单例持有一个常驻 PaddleOCR-json.exe 子进程，通过 stdin/stdout 管道喂图：
/// <list type="bullet">
///   <item>det/cls/rec 模型仅在首次调用 <see cref="EnsureRunningAsync"/> 时加载（约 600~1500 ms），
///         后续每次识别只跑推理（约 100~250 ms），相比旧的"每次 fork 进程 + 重新加载模型"快约 4~10 倍。</item>
///   <item><see cref="RecognizeAsync(byte[])"/> 走 image_base64 直接喂 stdin，零磁盘 IO，
///         这是当前唯一支持的输入路径（旧的 image_path 同步 / string 重载已删除）。</item>
///   <item>单例 + <see cref="SemaphoreSlim"/> 串行化保证 stdin/stdout 协议不会乱序，
///         进程异常退出时下次调用通过 <see cref="EnsureRunningAsync"/> 自动重启。</item>
///   <item>JobObject 保证宿主退出时子进程被 OS 兜底清理（即使 <see cref="Dispose"/> 未被调用）。</item>
/// </list>
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class OcrEngine : IOcrService, IDisposable
{
    private readonly string _engineExe;
    private readonly string _engineDir;
    private readonly JobObjectHelper _jobObject;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private Stream? _stdinStream;
    private StreamReader? _stdout;
    private bool _disposed;

    /// <summary>单次请求超时上限。进程卡死时不会无限阻塞调用方。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>JSON 属性名 "image_base64" 的 UTF-8 字节字面量（编译期常量，零分配）。</summary>
    private static ReadOnlySpan<byte> ImageBase64PropName => "image_base64"u8;

    /// <summary>优雅退出指令 + 换行（PaddleOCR-json 按行解析 stdin）。</summary>
    private static readonly byte[] ExitCommand = Encoding.UTF8.GetBytes("{\"exit\":1}\n");

    /// <summary>请求结束的换行字节（JSON 后追加，与旧 WriteLine 行为等价）。</summary>
    private static readonly byte[] NewlineBytes = new byte[] { (byte)'\n' };

    /// <param name="engineDir">ocr_engine 目录路径（含 PaddleOCR-json.exe），为空则自动查找。</param>
    public OcrEngine(string? engineDir = null)
    {
        _engineDir = engineDir ?? ResolveEngineDir();
        _engineExe = FindEngineExe(_engineDir);
        _jobObject = new JobObjectHelper();
    }

    /// <inheritdoc />
    public async Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return new List<OcrResult>();

        // image_base64 模式：图片字节直接通过 stdin 管道传输，零磁盘 IO。
        // Utf8JsonWriter 直接把 byte[] base64 编码成 UTF-8 字节写到 buffer，全程不产生中间 string。
        var json = await SendImageRequestAsync(imageBytes, CancellationToken.None).ConfigureAwait(false);
        return ParseResults(json);
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(byte[] imageBytes)
    {
        var results = await RecognizeAsync(imageBytes).ConfigureAwait(false);
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 实现：仅启动子进程并完成 stdin/stdout 管道连接,真正的模型加载在 PaddleOCR-json 接到
    /// 首个 image_base64 请求时才发生。因此预热同时压一张 1×1 透明 BMP 触发 det/cls/rec 模型加载,
    /// 把约 600~1500 ms 的冷启动成本摊到 App 启动阶段而非业务首次调用。
    /// </remarks>
    public async Task PrewarmAsync(CancellationToken ct = default)
    {
        // 若进程已经活着,模型必然已加载过(首次推理结束就驻留在内存),直接返回。
        if (_process is { HasExited: false } && _stdinStream != null && _stdout != null)
            return;

        // 触发一次最小推理(1×1 BMP),走和正常请求完全相同的代码路径,
        // 拿到响应后即认为模型加载完成。返回值丢弃。
        await SendImageRequestAsync(MinimalBmp, ct).ConfigureAwait(false);
    }

    /// <summary>1×1 32bpp 黑色 BMP,用于预热推理触发模型加载。</summary>
    private static readonly byte[] MinimalBmp = BuildMinimalBmp();

    private static byte[] BuildMinimalBmp()
    {
        // BITMAPFILEHEADER (14) + BITMAPINFOHEADER (40) + 1×1×4 像素 = 58 字节
        var bmp = new byte[58];
        var span = bmp.AsSpan();
        span[0] = (byte)'B'; span[1] = (byte)'M';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(2, 4), 58);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(10, 4), 54);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(14, 4), 40);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(18, 4), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(22, 4), -1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span.Slice(26, 2), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span.Slice(28, 2), 32);
        return bmp;
    }

    // ═══════════════════════════════════════════════
    //  常驻进程 IPC（stdin/stdout 单工请求-响应）
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 把图片字节作为 image_base64 请求写入 stdin，读取一行响应 JSON。
    /// gate 保证同一时间只有一个请求/响应对在管道里，
    /// 任何 IPC 异常都会触发 <see cref="ResetProcess"/>，下次调用自动重启进程。
    /// </summary>
    private async Task<string> SendImageRequestAsync(byte[] imageBytes, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureRunningAsync(ct).ConfigureAwait(false);

            // 单次请求超时保护：cancellationToken + 30 秒超时合并。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);
            var timeoutToken = timeoutCts.Token;

            try
            {
                await WriteImageRequestAsync(imageBytes, timeoutToken).ConfigureAwait(false);
                return await ReadJsonLineAsync(timeoutToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 超时但外部 ct 未取消 → 进程极可能卡死，强制重启。
                ResetProcess();
                throw new TimeoutException("PaddleOCR-json.exe 执行超时（30 秒）");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 任意 IPC 错误（管道关闭/进程崩溃/JSON 解析失败等）→ 重置进程，下次调用重启。
                ResetProcess();
                throw new InvalidOperationException("PaddleOCR-json IPC 失败", ex);
            }
        }
        finally
        {
            // _disposed=true 时 _gate 已被释放，跳过 Release 避免 ObjectDisposedException。
            if (!_disposed)
            {
                try { _gate.Release(); }
                catch (ObjectDisposedException) { /* Dispose 与请求竞态时容忍 */ }
            }
        }
    }

    /// <summary>
    /// 把 {"image_base64":"..."} 请求行直接写到 stdin 管道。
    /// <para>
    /// 旧实现路径：byte[] → Convert.ToBase64String (≈67KB string) → Dictionary → JsonSerializer.Serialize (再一份 67KB string)
    /// → StreamWriter.WriteLineAsync (char→UTF8 转码缓冲) = 三份大块堆分配 + 一次编码转换。
    /// </para>
    /// <para>
    /// 新实现：Utf8JsonWriter 直接把 byte[] 流式 Base64 编码到 ArrayBufferWriter（一份缓冲），再单次 WriteAsync 到 stdin BaseStream。
    /// </para>
    /// </summary>
    private async Task WriteImageRequestAsync(byte[] imageBytes, CancellationToken ct)
    {
        // Base64 输出大约是原长的 4/3，再加 JSON 框架字节（约 30B），预留点余量避免反复扩容。
        var initialCapacity = (imageBytes.Length * 4 / 3) + 64;
        var bufferWriter = new ArrayBufferWriter<byte>(initialCapacity);

        using (var writer = new Utf8JsonWriter(bufferWriter))
        {
            writer.WriteStartObject();
            writer.WriteBase64String(ImageBase64PropName, imageBytes);
            writer.WriteEndObject();
            // Utf8JsonWriter.Dispose 会 Flush 内部缓冲到 bufferWriter
        }

        var stream = _stdinStream!;
        await stream.WriteAsync(bufferWriter.WrittenMemory, ct).ConfigureAwait(false);
        // PaddleOCR-json 按行读 stdin，必须追加换行符。
        await stream.WriteAsync(NewlineBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 stdout 读取一行响应。PaddleOCR-json 启动后会先输出 banner/日志，
    /// 之后每个 stdin 请求严格对应一行 JSON 响应。忽略所有非 '{' 开头的行直到拿到 JSON。
    /// </summary>
    private async Task<string> ReadJsonLineAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await _stdout!.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null)
                throw new InvalidOperationException("PaddleOCR-json 子进程意外退出（stdout EOF）");

            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed[0] == '{')
                return trimmed;
            // 非 JSON 行（如 init banner、日志）直接丢弃。
        }
    }

    /// <summary>
    /// 确保常驻 PaddleOCR-json 进程处于可用状态。已存活则直接复用，否则重新启动。
    /// 调用方必须已持有 <see cref="_gate"/>，保证不会并发启动多个进程。
    /// </summary>
    private Task EnsureRunningAsync(CancellationToken ct)
    {
        if (_process is { HasExited: false } && _stdinStream != null && _stdout != null)
            return Task.CompletedTask;

        // 进程已死 / 字段不一致 → 清理残留后重启。
        ResetProcess();
        ct.ThrowIfCancellationRequested();

        var psi = new ProcessStartInfo
        {
            FileName = _engineExe,
            // 不传 -image_path：PaddleOCR-json 进入 stdin 持久化模式，
            // 每行 stdin 接收一个 JSON 请求，每次回吐一行 JSON 响应。
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
            WorkingDirectory = _engineDir,
        };

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动 PaddleOCR-json.exe: {_engineExe}");

        // entireProcessTree:true 配合 JobObject 兜底，宿主异常退出时不会留下孤儿进程。
        try
        {
            _jobObject.AssignProcess(proc.Handle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(OcrEngine)}] AssignProcess 失败: {ex.Message}");
        }

        // stderr 在后台异步排空：防止子进程把 4KB stderr 缓冲区写满后阻塞推理输出。
        _ = DrainStderrAsync(proc);

        _process = proc;
        // 直接持 BaseStream 跳过 StreamWriter 的 char→UTF8 转码缓冲：请求负载本身就是 UTF-8 字节。
        _stdinStream = proc.StandardInput.BaseStream;
        _stdout = proc.StandardOutput;
        return Task.CompletedTask;
    }

    private static async Task DrainStderrAsync(Process proc)
    {
        try
        {
            var reader = proc.StandardError;
            var buffer = new char[1024];
            while (await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false) > 0)
            {
                // 丢弃 stderr 内容即可；若需诊断可改 Debug.Write(buffer, 0, n)。
            }
        }
        catch
        {
            // 进程已死或管道关闭，自然退出后台任务。
        }
    }

    /// <summary>
    /// 强制关闭当前 PaddleOCR-json 子进程并清空相关字段。
    /// 异常路径（IPC 失败/超时）调用，让 <see cref="EnsureRunningAsync"/> 下次冷启动新进程。
    /// </summary>
    private void ResetProcess()
    {
        try { _stdinStream?.Dispose(); }
        catch (Exception ex) { Debug.WriteLine($"[{nameof(OcrEngine)}] dispose stdin failed: {ex.Message}"); }

        try { _stdout?.Dispose(); }
        catch (Exception ex) { Debug.WriteLine($"[{nameof(OcrEngine)}] dispose stdout failed: {ex.Message}"); }

        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) { Debug.WriteLine($"[{nameof(OcrEngine)}] kill process failed: {ex.Message}"); }

        try { _process?.Dispose(); }
        catch (Exception ex) { Debug.WriteLine($"[{nameof(OcrEngine)}] dispose process failed: {ex.Message}"); }

        _stdinStream = null;
        _stdout = null;
        _process = null;
    }

    // ═══════════════════════════════════════════════
    //  响应解析
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 将 PaddleOCR-json 返回的 JSON 字符串转换为 OcrResult 列表。
    /// </summary>
    private static List<OcrResult> ParseResults(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new List<OcrResult>();

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // PaddleOCR-json 状态码：100=成功, 101=无文字, >=200=错误
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 100)
            return new List<OcrResult>();

        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            return new List<OcrResult>();

        var items = new List<OcrResult>();
        foreach (var item in dataElement.EnumerateArray())
        {
            var box = item.GetProperty("box");
            var score = item.GetProperty("score").GetDouble();
            var text = item.GetProperty("text").GetString() ?? "";

            items.Add(new OcrResult
            {
                Text = text,
                Confidence = score,
                Box = new OcrBox
                {
                    TopLeft = new OcrPoint(box[0][0].GetInt32(), box[0][1].GetInt32()),
                    TopRight = new OcrPoint(box[1][0].GetInt32(), box[1][1].GetInt32()),
                    BottomRight = new OcrPoint(box[2][0].GetInt32(), box[2][1].GetInt32()),
                    BottomLeft = new OcrPoint(box[3][0].GetInt32(), box[3][1].GetInt32()),
                }
            });
        }

        return items;
    }

    // ═══════════════════════════════════════════════
    //  自动查找引擎目录
    // ═══════════════════════════════════════════════

    private static string FindEngineExe(string engineDir)
    {
        var exePath = Path.Combine(engineDir, "PaddleOCR-json.exe");
        if (File.Exists(exePath))
            return exePath;

        throw new InvalidOperationException(
            $"未找到 PaddleOCR-json.exe，请确保 {engineDir} 目录包含该文件。\n" +
            "下载地址：https://github.com/hiroi-sora/PaddleOCR-json/releases/latest");
    }

    private static string ResolveEngineDir()
    {
        var asmDir = AppDomain.CurrentDomain.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(asmDir, "ocr_engine"),
            Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "ocr_engine")),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "PaddleOCR-json.exe")))
                return candidate;
        }

        throw new InvalidOperationException(
            "未找到 ocr_engine 目录。请从 https://github.com/hiroi-sora/PaddleOCR-json/releases/latest " +
            "下载发行包并解压到 src\\ocr_engine\\ 目录。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // 优雅退出：发送 exit 命令，等待最多 500 ms 让进程自行收尾；
            // 超时则下方 ResetProcess 通过 Kill 强制终止。
            if (_process is { HasExited: false } && _stdinStream != null)
            {
                try
                {
                    _stdinStream.Write(ExitCommand, 0, ExitCommand.Length);
                    _stdinStream.Flush();
                    _process.WaitForExit(500);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(OcrEngine)}] graceful exit failed: {ex.Message}");
                }
            }
        }
        finally
        {
            ResetProcess();
            try { _gate.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[{nameof(OcrEngine)}] dispose gate failed: {ex.Message}"); }
            // JobObject 释放时，JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 会
            // 自动终止所有未退出的 PaddleOCR-json.exe 子进程（兜底）。
            _jobObject?.Dispose();
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
