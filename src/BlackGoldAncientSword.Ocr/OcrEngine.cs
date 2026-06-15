using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// PaddleOCR 引擎封装，通过子进程调用 PaddleOCR-json.exe（C++ 原生引擎）。
/// 启动时自动查找 ocr_engine 目录中的可执行文件。
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class OcrEngine : IOcrService, IDisposable
{
    private readonly string _engineExe;
    private readonly string _engineDir;
    private readonly JobObjectHelper _jobObject;

    /// <param name="engineDir">ocr_engine 目录路径（含 PaddleOCR-json.exe），为空则自动查找。</param>
    public OcrEngine(string? engineDir = null)
    {
        _engineDir = engineDir ?? ResolveEngineDir();
        _engineExe = FindEngineExe(_engineDir);
        _jobObject = new JobObjectHelper();
    }

    /// <inheritdoc />
    [Obsolete("使用 RecognizeAsync 替代；同步桥接保留仅为接口兼容，存在 ThreadPool 饥饿风险")]
    public List<OcrResult> Recognize(string imagePath)
    {
        // 同步 API 通过桥接 async 实现，保持单一执行通路。
        return RecognizeAsync(imagePath).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<List<OcrResult>> RecognizeAsync(string imagePath)
    {
        // ConfigureAwait(false)：同步桥接 Recognize(string) 通过 GetAwaiter().GetResult() 调用本方法，
        // 若 UI 线程同步等待会因捕获 SynchronizationContext 而死锁。
        var json = await InvokeOcrAsync(imagePath, CancellationToken.None).ConfigureAwait(false);
        return ParseResults(json);
    }

    /// <inheritdoc />
    public async Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes)
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpPath, imageBytes).ConfigureAwait(false);
            return await RecognizeAsync(tmpPath).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }

    /// <inheritdoc />
    public string RecognizeText(string imagePath)
    {
        // RecognizeText(string) 自身也是同步桥接 API，但接口要求保留；
        // 局部抑制 CS0618：本调用站点对同步桥接的依赖已被上层 Obsolete 标记暴露。
#pragma warning disable CS0618
        var results = Recognize(imagePath);
#pragma warning restore CS0618
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(string imagePath)
    {
        var results = await RecognizeAsync(imagePath).ConfigureAwait(false);
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(byte[] imageBytes)
    {
        var results = await RecognizeAsync(imageBytes).ConfigureAwait(false);
        return string.Join("\n", results.Select(r => r.Text));
    }

    // ═══════════════════════════════════════════════
    //  子进程调用 PaddleOCR-json.exe
    // ═══════════════════════════════════════════════

    private async Task<string> InvokeOcrAsync(string imagePath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _engineExe,
            Arguments = $"-image_path={imagePath}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = _engineDir,
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 PaddleOCR-json.exe");

            _jobObject.AssignProcess(process.Handle);

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // entireProcessTree:true 防止 PaddleOCR-json.exe 派生的子进程残留（.NET 5+ 支持）
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx) when (killEx is not OutOfMemoryException and not StackOverflowException)
                {
                    System.Diagnostics.Debug.WriteLine($"[{nameof(OcrEngine)}] Kill PaddleOCR 进程失败（可能已退出）: {killEx.Message}");
                }
                throw new TimeoutException("PaddleOCR-json.exe 执行超时（30 秒）");
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(
                    $"OCR 引擎异常退出 (ExitCode={process.ExitCode}): {error}");
            }

            return output;
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not TimeoutException and not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"调用 PaddleOCR-json.exe 失败: {_engineExe}", ex);
        }
    }

    /// <summary>
    /// 将 PaddleOCR-json 返回的 JSON 字符串转换为 OcrResult 列表。
    /// </summary>
    private static string? ExtractJsonLine(string rawOutput)
    {
        // PaddleOCR-json 输出格式：banner 行 + info 行 + ... + JSON 行
        // JSON 行以 "{" 开头，直接查找
        foreach (var line in rawOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('{'))
                return trimmed;
        }
        return null;
    }



    private static List<OcrResult> ParseResults(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return new List<OcrResult>();

        // PaddleOCR-json stdout 包含 banner 行，需要提取纯 JSON 行
        var json = ExtractJsonLine(rawOutput);
        if (json == null)
            return new List<OcrResult>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // PaddleOCR-json 状态码：100=成功, 101=无文字, >=200=错误
        var code = root.GetProperty("code").GetInt32();
        if (code != 100)
            return new List<OcrResult>();

        if (!root.TryGetProperty("data", out var dataElement))
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
        // JobObject 释放时，JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 会
        // 自动终止所有未退出的 PaddleOCR-json.exe 子进程。
        _jobObject?.Dispose();
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


