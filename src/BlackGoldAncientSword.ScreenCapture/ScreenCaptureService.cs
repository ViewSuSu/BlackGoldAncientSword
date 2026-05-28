using System.Diagnostics;
using System.Runtime.InteropServices;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.ScreenCapture;

[Component(ComponentLifetime.Singleton)]
public class ScreenCaptureService : IScreenCaptureService, IDisposable
{
    private bool _disposed;

    // ========================================================================
    //  Public API
    // ========================================================================

    public bool TryFindGameWindow(string processName, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (string.IsNullOrEmpty(processName)) return false;
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes)
        {
            if (p.MainWindowHandle != IntPtr.Zero)
            {
                hwnd = p.MainWindowHandle;
                return true;
            }
        }
        return false;
    }

    public byte[] CaptureWindow(IntPtr hwnd)
    {
        ThrowIfDisposed();
        if (hwnd == IntPtr.Zero) throw new ArgumentException("Invalid window handle", nameof(hwnd));
        if (!IsWindow(hwnd)) throw new ArgumentException("Window no longer exists", nameof(hwnd));
        return CaptureFrameInternal(hwnd, ScreenQuadrant.Full);
    }

    public Task<byte[]> CaptureWindowAsync(IntPtr hwnd) =>
        Task.Run(() => CaptureWindow(hwnd));

    public void CaptureWindowToFile(IntPtr hwnd, string filePath)
    {
        var bytes = CaptureWindow(hwnd);
        SaveBytes(bytes, filePath);
    }

    public async Task CaptureWindowToFileAsync(IntPtr hwnd, string filePath)
    {
        var bytes = await CaptureWindowAsync(hwnd);
        SaveBytes(bytes, filePath);
    }

    public void CaptureGame(string processName, string filePath)
    {
        var hwnd = ResolveGameWindow(processName);
        CaptureWindowToFile(hwnd, filePath);
    }

    public async Task CaptureGameAsync(string processName, string filePath)
    {
        var hwnd = ResolveGameWindow(processName);
        await CaptureWindowToFileAsync(hwnd, filePath);
    }

    public byte[] CaptureGame(string processName)
    {
        var hwnd = ResolveGameWindow(processName);
        return CaptureWindow(hwnd);
    }

    // ========================================================================
    //  Region / Quadrant capture
    // ========================================================================

    public byte[] CaptureRegion(IntPtr hwnd, ScreenQuadrant quadrants)
    {
        ThrowIfDisposed();
        if (quadrants == ScreenQuadrant.None)
            throw new ArgumentException("At least one quadrant must be specified", nameof(quadrants));
        if (hwnd == IntPtr.Zero) throw new ArgumentException("Invalid window handle", nameof(hwnd));
        if (!IsWindow(hwnd)) throw new ArgumentException("Window no longer exists", nameof(hwnd));
        return CaptureFrameInternal(hwnd, quadrants);
    }

    public Task<byte[]> CaptureRegionAsync(IntPtr hwnd, ScreenQuadrant quadrants) =>
        Task.Run(() => CaptureRegion(hwnd, quadrants));

    public void CaptureRegionToFile(IntPtr hwnd, ScreenQuadrant quadrants, string filePath)
    {
        var bytes = CaptureRegion(hwnd, quadrants);
        SaveBytes(bytes, filePath);
    }

    public async Task CaptureRegionToFileAsync(IntPtr hwnd, ScreenQuadrant quadrants, string filePath)
    {
        var bytes = await CaptureRegionAsync(hwnd, quadrants);
        SaveBytes(bytes, filePath);
    }

    public byte[] CaptureGameRegion(string processName, ScreenQuadrant quadrants)
    {
        var hwnd = ResolveGameWindow(processName);
        return CaptureRegion(hwnd, quadrants);
    }

    public void CaptureGameRegion(string processName, ScreenQuadrant quadrants, string filePath)
    {
        var hwnd = ResolveGameWindow(processName);
        CaptureRegionToFile(hwnd, quadrants, filePath);
    }

    public async Task CaptureGameRegionAsync(string processName, ScreenQuadrant quadrants, string filePath)
    {
        var hwnd = ResolveGameWindow(processName);
        await CaptureRegionToFileAsync(hwnd, quadrants, filePath);
    }

    // ========================================================================
    //  Capture pipeline: PrintWindow (occlusion-safe) with screen fallback
    // ========================================================================

    private byte[] CaptureFrameInternal(IntPtr hwnd, ScreenQuadrant quadrants)
    {
        if (!GetWindowRect(hwnd, out RECT rect))
            throw new InvalidOperationException("Failed to get window rectangle.");

        int fullWidth = rect.Right - rect.Left;
        int fullHeight = rect.Bottom - rect.Top;

        if (fullWidth <= 0 || fullHeight <= 0)
            throw new InvalidOperationException("Window has zero size.");

        var cropRect = CalculateCropRect(fullWidth, fullHeight, quadrants);

        // Get window DC to create compatible bitmap
        IntPtr hdcWindow = GetWindowDC(hwnd);
        if (hdcWindow == IntPtr.Zero)
            throw new InvalidOperationException("Failed to get window DC.");

        IntPtr hdcMem = CreateCompatibleDC(hdcWindow);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcWindow, fullWidth, fullHeight);
        IntPtr hOld = SelectObject(hdcMem, hBitmap);

        try
        {
            // Primary: PrintWindow with PW_RENDERFULLCONTENT — captures window
            // content even when occluded. Works with DWM-composited DirectX windows
            // on Windows 8.1+.
            const uint PW_RENDERFULLCONTENT = 0x00000002;
            bool captured = PrintWindow(hwnd, hdcMem, PW_RENDERFULLCONTENT);

            if (!captured)
            {
                // Fallback: screen-capture at window coordinates (legacy path)
                ReleaseDC(hwnd, hdcWindow);
                IntPtr hdcScreen = GetDC(IntPtr.Zero);
                if (hdcScreen == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to get screen DC.");
                try
                {
                    const uint SRCCOPY = 0x00CC0020;
                    const uint CAPTUREBLT = 0x40000000;
                    if (!BitBlt(hdcMem, 0, 0, fullWidth, fullHeight,
                                hdcScreen, rect.Left, rect.Top, SRCCOPY | CAPTUREBLT))
                    {
                        if (!BitBlt(hdcMem, 0, 0, fullWidth, fullHeight,
                                    hdcScreen, rect.Left, rect.Top, SRCCOPY))
                        {
                            throw new InvalidOperationException("BitBlt failed.");
                        }
                    }
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, hdcScreen);
                }
            }
        }
        finally
        {
            SelectObject(hdcMem, hOld);
            DeleteDC(hdcMem);
            ReleaseDC(hwnd, hdcWindow);
        }

        try
        {
            // Convert to WPF BitmapSource then PNG
            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            // Crop if needed
            if (cropRect.x != 0 || cropRect.y != 0 ||
                cropRect.width != fullWidth || cropRect.height != fullHeight)
            {
                bs = new System.Windows.Media.Imaging.CroppedBitmap(bs,
                    new System.Windows.Int32Rect(cropRect.x, cropRect.y,
                        cropRect.width, cropRect.height));
            }

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bs));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private static (int x, int y, int width, int height) CalculateCropRect(
        int fullWidth, int fullHeight, ScreenQuadrant quadrants)
    {
        if (quadrants == ScreenQuadrant.Full)
            return (0, 0, fullWidth, fullHeight);

        int midX = fullWidth / 2;
        int midY = fullHeight / 2;

        int minX = fullWidth;
        int minY = fullHeight;
        int maxX = 0;
        int maxY = 0;

        if (quadrants.HasFlag(ScreenQuadrant.TopLeft))
        {
            minX = Math.Min(minX, 0);
            minY = Math.Min(minY, 0);
            maxX = Math.Max(maxX, midX);
            maxY = Math.Max(maxY, midY);
        }
        if (quadrants.HasFlag(ScreenQuadrant.TopRight))
        {
            minX = Math.Min(minX, midX);
            minY = Math.Min(minY, 0);
            maxX = Math.Max(maxX, fullWidth);
            maxY = Math.Max(maxY, midY);
        }
        if (quadrants.HasFlag(ScreenQuadrant.BottomLeft))
        {
            minX = Math.Min(minX, 0);
            minY = Math.Min(minY, midY);
            maxX = Math.Max(maxX, midX);
            maxY = Math.Max(maxY, fullHeight);
        }
        if (quadrants.HasFlag(ScreenQuadrant.BottomRight))
        {
            minX = Math.Min(minX, midX);
            minY = Math.Min(minY, midY);
            maxX = Math.Max(maxX, fullWidth);
            maxY = Math.Max(maxY, fullHeight);
        }

        int cropWidth = maxX - minX;
        int cropHeight = maxY - minY;
        return (minX, minY, cropWidth, cropHeight);
    }

    // ========================================================================
    //  Helpers
    // ========================================================================

    private static IntPtr ResolveGameWindow(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            throw new ArgumentException("Process name cannot be empty", nameof(processName));
        if (!TryFindGameWindowStatic(processName, out var hwnd))
            throw new InvalidOperationException(
                $"Game process \"{processName}\" not found or has no visible window.");
        return hwnd;
    }

    private static bool TryFindGameWindowStatic(string processName, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes)
        {
            if (p.MainWindowHandle != IntPtr.Zero)
            {
                hwnd = p.MainWindowHandle;
                return true;
            }
        }
        return false;
    }

    private static void SaveBytes(byte[] bytes, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(filePath, bytes);
    }

    // ========================================================================
    //  P/Invoke
    // ========================================================================

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hDestDC, int x, int y, int width, int height,
        IntPtr hSrcDC, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // ========================================================================
    //  Disposal
    // ========================================================================

    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ScreenCaptureService));
    }
}
