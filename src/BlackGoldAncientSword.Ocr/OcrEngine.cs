using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// PaddleOCR �����װ��ͨ�� Python.NET ���������� PaddleOCR��
/// ����ʱ�Զ��������������Ŀ¼�е� Python ������
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class OcrEngine : IOcrService, IDisposable
{
    private readonly string _pythonHome;
    private readonly string _ocrServiceDir;
    private dynamic? _ocrInstance;
    private bool _initialized;

    /// <param name="pythonHome">Python ��װĿ¼·����������Զ���������档</param>
    /// <param name="ocrServiceDir">ocr_service �ű�Ŀ¼·����������Զ����ҡ�</param>
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

    // �T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T
    //  Python.NET ��ʼ��
    // �T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T

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
    /// ���� PaddleOCR ���ص� Python ����Ϊ C# ǿ�����б��
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

    // �T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T
    //  �Զ��������� Python �� OCR ����Ŀ¼
    // �T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T

    private static string FindPythonDll(string pythonHome)
    {
        foreach (var dll in Directory.EnumerateFiles(pythonHome, "python3*.dll"))
        {
            return dll;
        }

        throw new InvalidOperationException(
            $"������ Python Ŀ¼��δ�ҵ� python3*.dll: {pythonHome}�������� setup_python_env.ps1 ���¹�����");
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
            "δ�ҵ������ Python �������������� src\\ocr_service\\setup_python_env.ps1 ������");
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
            "δ�ҵ� ocr_service Ŀ¼����ȷ�� ocr_service\\ �Ѹ��Ƶ����Ŀ¼��");
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

// �T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T
//  OCR ������ݽṹ
// �T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T

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
