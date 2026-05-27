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
    public List<OcrResult> Recognize(string imagePath)
    {
        var json = InvokeOcr(imagePath);
        return ParseResults(json);
    }

    /// <inheritdoc />
    public async Task<List<OcrResult>> RecognizeAsync(string imagePath)
    {
        return await Task.Run(() => Recognize(imagePath));
    }

    /// <inheritdoc />
    public List<OcrResult> Recognize(byte[] imageBytes)
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpPath, imageBytes);
            return Recognize(tmpPath);
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }

    /// <inheritdoc />
    public async Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes)
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpPath, imageBytes);
            return Recognize(tmpPath);
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
        var results = Recognize(imagePath);
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(string imagePath)
    {
        var results = await RecognizeAsync(imagePath);
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    public string RecognizeText(byte[] imageBytes)
    {
        var results = Recognize(imageBytes);
        return string.Join("\n", results.Select(r => r.Text));
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(byte[] imageBytes)
    {
        var results = await RecognizeAsync(imageBytes);
        return string.Join("\n", results.Select(r => r.Text));
    }

    // ═══════════════════════════════════════════════
    //  子进程调用 PaddleOCR-json.exe
    // ═══════════════════════════════════════════════

    private string InvokeOcr(string imagePath)
    {
        // PaddleOCR-json.exe 对非 ASCII 路径支持不佳，始终通过临时文件传递
        string safePath = imagePath;
        bool useTemp = ContainsNonAscii(imagePath);
        if (useTemp)
        {
            safePath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.png");
            File.Copy(imagePath, safePath, overwrite: true);
        }

        try
        {
            return InvokeOcrInternal(safePath);
        }
        finally
        {
            if (useTemp && File.Exists(safePath))
                File.Delete(safePath);
        }
    }

    private string InvokeOcrInternal(string imagePath)
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

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                throw new TimeoutException("PaddleOCR-json.exe 执行超时（30 秒）");
            }

            var output = outputTask.Result;
            var error = errorTask.Result;

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(
                    $"OCR 引擎异常退出 (ExitCode={process.ExitCode}): {error}");
            }

            return output;
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not TimeoutException)
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

    private static bool ContainsNonAscii(string path)
    {
        foreach (char c in path)
        {
            if (c > 127) return true;
        }
        return false;
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



