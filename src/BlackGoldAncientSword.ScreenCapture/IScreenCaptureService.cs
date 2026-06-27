namespace BlackGoldAncientSword.ScreenCapture;

/// <summary>
/// 游戏截屏服务接口。提供截取游戏窗口画面并保存为图片字节/文件的能力。
/// 支持按四象限（上下左右）截取窗口子区域。
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

    // ─────────────────── Region / Quadrant capture ───────────────────

    /// <summary>
    /// 截取窗口的指定象限区域，返回 PNG 字节数组。
    /// 例如 <c>ScreenQuadrant.BottomRight</c> 获取右下角，
    /// <c>ScreenQuadrant.BottomLeft | ScreenQuadrant.BottomRight</c> 获取下半部分。
    /// </summary>
    byte[] CaptureRegion(IntPtr hwnd, ScreenQuadrant quadrants);

    /// <summary>异步截取窗口的指定象限区域，返回 PNG 字节数组。</summary>
    Task<byte[]> CaptureRegionAsync(IntPtr hwnd, ScreenQuadrant quadrants);

    /// <summary>截取窗口的指定象限区域并保存到文件。</summary>
    void CaptureRegionToFile(IntPtr hwnd, ScreenQuadrant quadrants, string filePath);

    /// <summary>异步截取窗口的指定象限区域并保存到文件。</summary>
    Task CaptureRegionToFileAsync(IntPtr hwnd, ScreenQuadrant quadrants, string filePath);

    /// <summary>根据进程名截取游戏指定象限区域，返回 PNG 字节数组。</summary>
    byte[] CaptureGameRegion(string processName, ScreenQuadrant quadrants);

    /// <summary>根据进程名截取游戏指定象限区域并保存到文件。</summary>
    void CaptureGameRegion(string processName, ScreenQuadrant quadrants, string filePath);

    /// <summary>异步根据进程名截取游戏指定象限区域并保存到文件。</summary>
    Task CaptureGameRegionAsync(string processName, ScreenQuadrant quadrants, string filePath);

    // ─────────────────── Raw pixel capture ───────────────────

    /// <summary>
    /// 截取整个窗口，返回原始 BGRA 像素数据（不编码为 PNG），避免多次截图时的重复开销。
    /// </summary>
    byte[] CaptureFullRaw(IntPtr hwnd, out int width, out int height);

    /// <summary>
    /// 从原始 BGRA 像素数据中按指定象限裁剪并编码为 PNG。
    /// </summary>
    byte[] CropRawToPng(byte[] rawBgra, int fullWidth, int fullHeight, ScreenQuadrant quadrants);
}
