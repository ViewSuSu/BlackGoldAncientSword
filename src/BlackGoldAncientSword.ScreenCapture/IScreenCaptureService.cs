namespace BlackGoldAncientSword.ScreenCapture;

/// <summary>
/// 游戏截屏服务接口。提供截取游戏窗口画面并保存为图片字节/文件的能力。
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>通过进程名查找游戏窗口句柄。</summary>
    bool TryFindGameWindow(string processName, out IntPtr hwnd);

    /// <summary>截取指定窗口画面，返回 PNG 字节数组。</summary>
    byte[] CaptureWindow(IntPtr hwnd);

    /// <summary>异步截取指定窗口画面，返回 PNG 字节数组。</summary>
    Task<byte[]> CaptureWindowAsync(IntPtr hwnd);

    /// <summary>截取指定窗口并保存到文件。</summary>
    void CaptureWindowToFile(IntPtr hwnd, string filePath);

    /// <summary>异步截取指定窗口并保存到文件。</summary>
    Task CaptureWindowToFileAsync(IntPtr hwnd, string filePath);

    /// <summary>根据进程名直接截取游戏画面并保存到文件。</summary>
    void CaptureGame(string processName, string filePath);

    /// <summary>异步根据进程名直接截取游戏画面并保存到文件。</summary>
    Task CaptureGameAsync(string processName, string filePath);

    /// <summary>根据进程名直接截取游戏画面，返回 PNG 字节数组。</summary>
    byte[] CaptureGame(string processName);
}
