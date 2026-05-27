using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;

namespace NarakaBladepoint.StatsAssistant.Ocr;

/// <summary>
/// PaddleOCR 引擎封装。通过 Python.NET 互操作调用 PaddleOCR。
/// 运行时自动发现捆绑在输出目录中的 Python 环境。
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class OcrEngine : IOcrService, IDisposable
{
    private readonly string _pythonHome;
    private readonly string _ocrServiceDir;
    private dynamic? _ocrInstance;
    private bool _initialized;

    /// <param name="pythonHome">Python 安装目录路径。留空则自动查找捆绑版。</param>
    /// <param name="ocrServiceDir">ocr_service 脚本目录路径。留空则自动查找。</param>
    public OcrEngine(string? pythonHome = null, string? ocrServiceDir = null)
    {
        _pythonHome = pythonHome ?? ResolvePythonHome();
        _ocrServiceDir = ocrServiceDir ?? ResolveOcrServiceDir();
    }

    /// <inheritdoc />
    public List<OcrResult> Recognize(string imagePath)
    {
        EnsureInitialized();
        using (Py.GIL())
        {
            dynamic result = _ocrInstance!.recognize(imagePath);
            return ParseResults(result);
        }
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
    //  Python.NET 初始化
    // ═══════════════════════════════════════════════

    private void EnsureInitialized()
    {
        if (_initialized) return;

        var pythonDll = FindPythonDll(_pythonHome);
        Runtime.PythonDLL = pythonDll;

        Environment.SetEnvironmentVariable("PYTHONHOME", _pythonHome);
        Environment.SetEnvironmentVariable("PYTHONPATH", _ocrServiceDir);
        Environment.SetEnvironmentVariable("PATH",
            _pythonHome + ";" + Path.Combine(_pythonHome, "Scripts") + ";" +
            Environment.GetEnvironmentVariable("PATH"));

        PythonEngine.PythonHome = _pythonHome;
        PythonEngine.Initialize();

        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.insert(0, _ocrServiceDir);
            dynamic ocrModule = Py.Import("ocr_engine");
            _ocrInstance = ocrModule.get_ocr();
        }

        _initialized = true;
    }

    /// <summary>
    /// 解析 PaddleOCR 返回的 Python 对象为 C# 强类型列表。
    /// </summary>
    private static List<OcrResult> ParseResults(dynamic result)
    {
        var items = new List<OcrResult>();
        if (result == null) return items;

        try
        {
            foreach (var page in result)
            {
                if (page == null) continue;
                foreach (dynamic item in page)
                {
                    items.Add(new OcrResult
                    {
                        Text = item[1][0].As<string>(),
                        Confidence = item[1][1].As<double>(),
                        Box = new OcrBox
                        {
                            TopLeft = new OcrPoint(item[0][0][0].As<int>(), item[0][0][1].As<int>()),
                            TopRight = new OcrPoint(item[0][1][0].As<int>(), item[0][1][1].As<int>()),
                            BottomRight = new OcrPoint(item[0][2][0].As<int>(), item[0][2][1].As<int>()),
                            BottomLeft = new OcrPoint(item[0][3][0].As<int>(), item[0][3][1].As<int>()),
                        }
                    });
                }
            }
        }
        catch { }

        return items;
    }

    // ═══════════════════════════════════════════════
    //  自动发现捆绑 Python 和 OCR 服务目录
    // ═══════════════════════════════════════════════

    private static string FindPythonDll(string pythonHome)
    {
        foreach (var dll in Directory.EnumerateFiles(pythonHome, "python3*.dll"))
        {
            return dll;
        }

        throw new InvalidOperationException(
            $"在捆绑 Python 目录中未找到 python3*.dll: {pythonHome}。请运行 setup_python_env.ps1 重新构建。");
    }

    private static string ResolvePythonHome()
    {
        var asmDir = AppDomain.CurrentDomain.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(asmDir, "ocr_service", "python"),
            Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "ocr_service", "python")),
        };

        foreach (var path in candidates)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    FindPythonDll(path);
                    return path;
                }
                catch { }
            }
        }

        throw new InvalidOperationException(
            "未找到捆绑的 Python 环境。请先运行 src\\ocr_service\\setup_python_env.ps1 构建。");
    }

    private static string ResolveOcrServiceDir()
    {
        var asmDir = AppDomain.CurrentDomain.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(asmDir, "ocr_service"),
            Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "ocr_service")),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "ocr_engine.py")))
                return candidate;
        }

        throw new InvalidOperationException(
            "未找到 ocr_service 目录。请确保 ocr_service\\ 已复制到输出目录。");
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _ocrInstance = null;
            PythonEngine.Shutdown();
            _initialized = false;
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
