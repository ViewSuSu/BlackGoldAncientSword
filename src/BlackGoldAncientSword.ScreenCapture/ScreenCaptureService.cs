using System.Diagnostics;
using System.Runtime.InteropServices;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.ScreenCapture;

[Component(ComponentLifetime.Singleton)]
public class ScreenCaptureService : IScreenCaptureService, IDisposable
{
    private bool _disposed;
    private IntPtr _d3dDevicePtr;
    private SharpDX.Direct3D11.Device? _sharpDxDevice;
    private SharpDX.Direct3D11.DeviceContext? _sharpDxCtx;
    private static bool _nativeWgcAvailable = true;

    public bool TryFindGameWindow(string processName, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (string.IsNullOrEmpty(processName)) return false;
        var procs = Process.GetProcessesByName(processName);
        try
        {
            foreach (var p in procs)
            {
                if (p.MainWindowHandle != IntPtr.Zero) { hwnd = p.MainWindowHandle; return true; }
            }
            return false;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }

    public byte[] CaptureWindow(IntPtr hwnd) { ThrowIfDisposed(); ValidateHwnd(hwnd); return CaptureFrameInternal(hwnd, ScreenQuadrant.Full); }
    public Task<byte[]> CaptureWindowAsync(IntPtr hwnd) => Task.Run(() => CaptureWindow(hwnd));
    public void CaptureWindowToFile(IntPtr hwnd, string filePath) { SaveBytes(CaptureWindow(hwnd), filePath); }
    public async Task CaptureWindowToFileAsync(IntPtr hwnd, string filePath) { SaveBytes(await CaptureWindowAsync(hwnd), filePath); }
    public void CaptureGame(string processName, string filePath) { CaptureWindowToFile(ResolveGameWindow(processName), filePath); }
    public byte[] CaptureGame(string processName) => CaptureWindow(ResolveGameWindow(processName));
    public async Task CaptureGameAsync(string processName, string filePath) { var h = ResolveGameWindow(processName); SaveBytes(await CaptureWindowAsync(h), filePath); }
    public byte[] CaptureGameRegion(string processName, ScreenQuadrant q) => CaptureRegion(ResolveGameWindow(processName), q);
    public void CaptureGameRegion(string processName, ScreenQuadrant q, string filePath) { SaveBytes(CaptureRegion(ResolveGameWindow(processName), q), filePath); }
    public async Task CaptureGameRegionAsync(string processName, ScreenQuadrant q, string filePath) { SaveBytes(await CaptureRegionAsync(ResolveGameWindow(processName), q), filePath); }
    public async Task CaptureRegionToFileAsync(IntPtr hwnd, ScreenQuadrant q, string filePath) { SaveBytes(await CaptureRegionAsync(hwnd, q), filePath); }

    public byte[] CaptureRegion(IntPtr hwnd, ScreenQuadrant quadrants)
    { ThrowIfDisposed(); if (quadrants == ScreenQuadrant.None) throw new ArgumentException("quadrants"); ValidateHwnd(hwnd); return CaptureFrameInternal(hwnd, quadrants); }
    public Task<byte[]> CaptureRegionAsync(IntPtr hwnd, ScreenQuadrant quadrants) => Task.Run(() => CaptureRegion(hwnd, quadrants));
    public void CaptureRegionToFile(IntPtr hwnd, ScreenQuadrant quadrants, string filePath) { SaveBytes(CaptureRegion(hwnd, quadrants), filePath); }

    private void ValidateHwnd(IntPtr hwnd) { if (hwnd == IntPtr.Zero) throw new ArgumentException("Invalid hwnd"); if (!IsWindow(hwnd)) throw new ArgumentException("Window gone"); }

    private byte[] CaptureFrameInternal(IntPtr hwnd, ScreenQuadrant quadrants)
    {
        if (!GetWindowRect(hwnd, out RECT wr)) throw new InvalidOperationException("GetWindowRect failed");
        int winW = wr.Right - wr.Left, winH = wr.Bottom - wr.Top;
        if (winW <= 0 || winH <= 0) throw new InvalidOperationException("Zero size window");

        // 检测窗口化模式：有标题栏时需要排除标题栏区域
        int clientW = winW, clientH = winH;
        int titleBarOffsetY = 0;
        int borderOffsetX = 0;
        if (IsWindowedWithTitleBar(hwnd))
        {
            GetClientRect(hwnd, out RECT cr);
            var pt = new POINT();
            ClientToScreen(hwnd, ref pt);
            borderOffsetX = pt.X - wr.Left;
            titleBarOffsetY = pt.Y - wr.Top;
            clientW = cr.Right - cr.Left;
            clientH = cr.Bottom - cr.Top;

            // 游戏始终 16:9 渲染，非 16:9 窗口会有黑边，裁剪到有效游戏区域
            double clientAspect = (double)clientW / clientH;
            const double GameAspect = 16.0 / 9.0;
            if (Math.Abs(clientAspect - GameAspect) > 0.01)
            {
                if (clientAspect > GameAspect)
                {
                    int effectiveW = (int)(clientH * GameAspect);
                    borderOffsetX += (clientW - effectiveW) / 2;
                    clientW = effectiveW;
                }
                else
                {
                    int effectiveH = (int)(clientW / GameAspect);
                    titleBarOffsetY += (clientH - effectiveH) / 2;
                    clientH = effectiveH;
                }
            }
        }

        // 以客户区尺寸计算裁剪区域，再偏移到完整窗口坐标系
        var crop = CalcCrop(clientW, clientH, quadrants);
        crop.x += borderOffsetX;
        crop.y += titleBarOffsetY;
        Console.WriteLine($"[SC] Win=({wr.Left},{wr.Top}) {winW}x{winH} Client={clientW}x{clientH} TitleBarY={titleBarOffsetY} BorderX={borderOffsetX} Crop=({crop.x},{crop.y} {crop.w}x{crop.h})");

        // 1. Native WGC DLL (occlusion-free)
        if (_nativeWgcAvailable)
        {
            try
            {
                var capResult = NativeWgc.Capture(hwnd, winW, winH, crop);
                if (capResult != null) { var (data, cw, ch) = capResult.Value; Console.WriteLine("[SC] Native WGC OK"); return BgraToPng(data, cw, ch); }
            }
            catch (DllNotFoundException) { Console.WriteLine("[SC] wgc_capture.dll not found"); _nativeWgcAvailable = false; }
            catch (Exception ex) { Console.WriteLine($"[SC] Native WGC: {ex.Message}"); }
        }

        // 2. COM vtable WGC (occlusion-free, if interop available)
        try
        {
            return CaptureViaWgcCom(hwnd, winW, winH, crop);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SC] COM WGC failed ({ex.GetType().Name}: {ex.Message}), GDI fallback...");
        }

        // 3. GDI fallback
        return CaptureViaGdi(hwnd, wr, winW, winH, crop);
    }

    private byte[] CaptureViaWgcCom(IntPtr hwnd, int w, int h, (int x, int y, int w, int h) c)
    {
        EnsureD3DDevice();
        IntPtr item = IntPtr.Zero, pool = IntPtr.Zero, sess = IntPtr.Zero, frame = IntPtr.Zero, surf = IntPtr.Zero, dxgi = IntPtr.Zero;
        try
        {
            item = WgcInterop.CreateCaptureItemForWindow(hwnd);
            pool = WgcInterop.CreateFreeThreadedFramePool(_d3dDevicePtr, w, h);
            sess = WgcInterop.CreateCaptureSession(pool, _d3dDevicePtr, item);
            WgcInterop.StartCapture(sess);
            frame = WgcInterop.WaitForFrame(pool, 5000);
            surf = WgcInterop.GetFrameSurface(frame);
            dxgi = WgcInterop.GetDxgiInterface(surf, typeof(SharpDX.DXGI.Surface).GUID);
            return ReadDxgiSurface(dxgi, w, h, c);
        }
        finally
        {
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (surf != IntPtr.Zero) Marshal.Release(surf);
            if (frame != IntPtr.Zero) Marshal.Release(frame);
            if (sess != IntPtr.Zero) { try { WgcInterop.StopCapture(sess); } catch { } Marshal.Release(sess); }
            if (pool != IntPtr.Zero) Marshal.Release(pool);
            if (item != IntPtr.Zero) Marshal.Release(item);
        }
    }

    private void EnsureD3DDevice()
    {
        if (_d3dDevicePtr != IntPtr.Zero) return;
        _sharpDxDevice = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);
        _sharpDxCtx = _sharpDxDevice.ImmediateContext;
        using var d = _sharpDxDevice.QueryInterface<SharpDX.DXGI.Device>();
        int hr = CreateDirect3D11DeviceFromDXGIDevice(d.NativePointer, out _d3dDevicePtr);
        if (hr != 0) throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice: 0x{hr:X8}");
        Console.WriteLine($"[SC] D3D WinRT device: 0x{_d3dDevicePtr:X}");
    }

    private byte[] ReadDxgiSurface(IntPtr dxgiPtr, int w, int h, (int x, int y, int w, int h) c)
    {
        var s = new SharpDX.DXGI.Surface(dxgiPtr);
        try
        {
            var desc = new SharpDX.Direct3D11.Texture2DDescription { Width = w, Height = h, MipLevels = 1, ArraySize = 1, Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm, SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0), Usage = SharpDX.Direct3D11.ResourceUsage.Staging, BindFlags = SharpDX.Direct3D11.BindFlags.None, CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read, OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None };
            var st = new SharpDX.Direct3D11.Texture2D(_sharpDxDevice!, desc);
            _sharpDxCtx!.CopyResource(st, s.QueryInterface<SharpDX.Direct3D11.Texture2D>());
            var map = _sharpDxCtx.MapSubresource(st, 0, SharpDX.Direct3D11.MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            try
            {
                int stride = map.RowPitch;
                var fd = new byte[w * h * 4];
                if (stride == w * 4) Marshal.Copy(map.DataPointer, fd, 0, fd.Length);
                else for (int y = 0; y < h; y++) Marshal.Copy(map.DataPointer + y * stride, fd, y * w * 4, w * 4);
                var r = new byte[c.w * c.h * 4]; int fs = w * 4, cs = c.w * 4;
                for (int y = 0; y < c.h; y++) Array.Copy(fd, (c.y + y) * fs + c.x * 4, r, y * cs, cs);
                return BgraToPng(r, c.w, c.h);
            }
            finally { _sharpDxCtx.UnmapSubresource(st, 0); st.Dispose(); }
        }
        finally { s.Dispose(); }
    }

    private static byte[] CaptureViaGdi(IntPtr hwnd, RECT wr, int w, int h, (int x, int y, int w, int h) c)
    {
        IntPtr hdcW = GetWindowDC(hwnd); if (hdcW == IntPtr.Zero) throw new InvalidOperationException("GetWindowDC");
        IntPtr hdcM = CreateCompatibleDC(hdcW), hbmp = CreateCompatibleBitmap(hdcW, w, h), ho = SelectObject(hdcM, hbmp);
        try
        {
            bool printed = PrintWindow(hwnd, hdcM, 0x2);
            if (!printed) { ReleaseDC(hwnd, hdcW); hdcW = IntPtr.Zero; IntPtr hdcS = GetDC(IntPtr.Zero); try { if (!BitBlt(hdcM, 0, 0, w, h, hdcS, wr.Left, wr.Top, 0x40CC0020) && !BitBlt(hdcM, 0, 0, w, h, hdcS, wr.Left, wr.Top, 0x00CC0020)) throw new InvalidOperationException("BitBlt"); } finally { ReleaseDC(IntPtr.Zero, hdcS); } }
        }
        finally { SelectObject(hdcM, ho); DeleteDC(hdcM); if (hdcW != IntPtr.Zero) ReleaseDC(hwnd, hdcW); }
        try { return BitmapToPng(hbmp, w, h, c); }
        finally { DeleteObject(hbmp); }
    }

    private static byte[] BitmapToPng(IntPtr hbmp, int w, int h, (int x, int y, int w, int h) c)
    {
        IntPtr hdcS = GetDC(IntPtr.Zero);
        try
        {
            IntPtr hdcM = CreateCompatibleDC(hdcS); IntPtr ho2 = SelectObject(hdcM, hbmp);
            var bi = new BITMAPINFO { biHeader = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
            var fp = new byte[w * h * 4]; if (GetDIBits(hdcM, hbmp, 0, (uint)h, fp, ref bi, 0) == 0) throw new InvalidOperationException("GetDIBits");
            SelectObject(hdcM, ho2); DeleteDC(hdcM);
            var r = new byte[c.w * c.h * 4]; int fs = w * 4, cs = c.w * 4;
            for (int y = 0; y < c.h; y++) Array.Copy(fp, (c.y + y) * fs + c.x * 4, r, y * cs, cs);
            return BgraToPng(r, c.w, c.h);
        }
        finally { ReleaseDC(IntPtr.Zero, hdcS); }
    }

    private static byte[] BgraToPng(byte[] d, int w, int h)
    { using var ms = new MemoryStream(); var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, d, w * 4))); enc.Save(ms); return ms.ToArray(); }

    /// <summary>
    /// 检测窗口是否处于有标题栏的窗口化模式（WS_CAPTION 样式）。
    /// 无边框全屏和独占全屏模式没有标题栏，返回 false。
    /// </summary>
    private static bool IsWindowedWithTitleBar(IntPtr hwnd)
    {
        const int GWL_STYLE = -16;
        const uint WS_CAPTION = 0x00C00000;
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        return ((ulong)style & WS_CAPTION) != 0;
    }

    /// <summary>
    /// 从原始 BGRA 像素数据中裁剪指定区域。
    /// </summary>
    private static byte[] CropRawPixels(byte[] rawBgra, int srcWidth, int srcHeight,
        int cropX, int cropY, int cropW, int cropH)
    {
        if (cropW <= 0 || cropH <= 0)
            throw new ArgumentException("Crop region has zero size");
        var result = new byte[cropW * cropH * 4];
        int srcStride = srcWidth * 4;
        int dstStride = cropW * 4;
        for (int y = 0; y < cropH; y++)
            Array.Copy(rawBgra, (cropY + y) * srcStride + cropX * 4, result, y * dstStride, dstStride);
        return result;
    }

    private static (int x, int y, int w, int h) CalcCrop(int w, int h, ScreenQuadrant q)
    {
        if (q == ScreenQuadrant.Full) return (0, 0, w, h);
        int mx = w / 2, my = h / 2, x = 0, y = 0, rw = w, rh = h;
        bool hl = q.HasFlag(ScreenQuadrant.TopLeft) || q.HasFlag(ScreenQuadrant.BottomLeft), hr2 = q.HasFlag(ScreenQuadrant.TopRight) || q.HasFlag(ScreenQuadrant.BottomRight), ht = q.HasFlag(ScreenQuadrant.TopLeft) || q.HasFlag(ScreenQuadrant.TopRight), hb = q.HasFlag(ScreenQuadrant.BottomLeft) || q.HasFlag(ScreenQuadrant.BottomRight);
        if (hl && !hr2) { x = 0; rw = mx; } if (hr2 && !hl) { x = mx; rw = w - mx; } if (ht && !hb) { y = 0; rh = my; } if (hb && !ht) { y = my; rh = h - my; }
        return (x, y, rw, rh);
    }

    private static IntPtr ResolveGameWindow(string n) { if (string.IsNullOrEmpty(n)) throw new ArgumentException("name"); if (!TryFindWindow(n, out var h)) throw new InvalidOperationException($"Not found: {n}"); return h; }
    private static bool TryFindWindow(string n, out IntPtr h)
    {
        h = IntPtr.Zero;
        var procs = Process.GetProcessesByName(n);
        try
        {
            foreach (var p in procs)
                if (p.MainWindowHandle != IntPtr.Zero) { h = p.MainWindowHandle; return true; }
            return false;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }
    private static void SaveBytes(byte[] b, string p) { var d = Path.GetDirectoryName(p); if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d); File.WriteAllBytes(p, b); }

    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr GetWindowLongPtr(IntPtr h, int nIndex);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr ddc, int x, int y, int w, int h, IntPtr sdc, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint s, uint c, byte[] bits, ref BITMAPINFO bi, uint u);
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr d, out IntPtr o);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO { public BITMAPINFOHEADER biHeader; }

    public byte[] CaptureFullRaw(IntPtr hwnd, out int width, out int height)
    {
        ThrowIfDisposed();
        ValidateHwnd(hwnd);
        if (!GetWindowRect(hwnd, out RECT wr))
            throw new InvalidOperationException("GetWindowRect failed");
        int winW = wr.Right - wr.Left;
        int winH = wr.Bottom - wr.Top;
        if (winW <= 0 || winH <= 0)
            throw new InvalidOperationException("Zero size window");

        // 检测窗口化模式，排除标题栏：只截取客户区
        int cropX = 0, cropY = 0, cropW = winW, cropH = winH;
        if (IsWindowedWithTitleBar(hwnd))
        {
            GetClientRect(hwnd, out RECT cr);
            var pt = new POINT();
            ClientToScreen(hwnd, ref pt);
            cropX = pt.X - wr.Left;
            cropY = pt.Y - wr.Top;
            cropW = cr.Right - cr.Left;
            cropH = cr.Bottom - cr.Top;

            // 游戏始终 16:9 渲染，裁剪到有效游戏区域（排除黑边）
            double clientAspect = (double)cropW / cropH;
            const double GameAspect = 16.0 / 9.0;
            if (Math.Abs(clientAspect - GameAspect) > 0.01)
            {
                if (clientAspect > GameAspect)
                {
                    int effectiveW = (int)(cropH * GameAspect);
                    cropX += (cropW - effectiveW) / 2;
                    cropW = effectiveW;
                }
                else
                {
                    int effectiveH = (int)(cropW / GameAspect);
                    cropY += (cropH - effectiveH) / 2;
                    cropH = effectiveH;
                }
            }
        }

        Console.WriteLine($"[SC] CaptureFullRaw: Win=({wr.Left},{wr.Top}) {winW}x{winH} Client={cropW}x{cropH} TitleBarY={cropY}");

        // 1. Native WGC DLL
        if (_nativeWgcAvailable)
        {
            try
            {
                var capResult = NativeWgc.Capture(hwnd, winW, winH, (cropX, cropY, cropW, cropH));
                if (capResult != null)
                {
                    var (data, w, h) = capResult.Value;
                    width = w;
                    height = h;
                    Console.WriteLine($"[SC] CaptureFullRaw: Native WGC OK {w}x{h}");
                    return data;
                }
            }
            catch (DllNotFoundException) { Console.WriteLine("[SC] wgc_capture.dll not found"); _nativeWgcAvailable = false; }
            catch (Exception ex) { Console.WriteLine($"[SC] Native WGC: {ex.Message}"); }
        }

        // 2. Fallback: CaptureWindow 已通过 CaptureFrameInternal 排除标题栏和黑边
        Console.WriteLine("[SC] CaptureFullRaw: falling back to PNG decode path");
        var pngBytes = CaptureWindow(hwnd);
        return PngToBgra(pngBytes, out width, out height);
    }

    public byte[] CropRawToPng(byte[] rawBgra, int fullWidth, int fullHeight, ScreenQuadrant quadrants)
    {
        var crop = CalcCrop(fullWidth, fullHeight, quadrants);
        if (crop.w <= 0 || crop.h <= 0)
            throw new ArgumentException("Crop region has zero size");

        var result = new byte[crop.w * crop.h * 4];
        int fullStride = fullWidth * 4;
        int cropStride = crop.w * 4;
        for (int y = 0; y < crop.h; y++)
            Array.Copy(rawBgra, (crop.y + y) * fullStride + crop.x * 4, result, y * cropStride, cropStride);

        return BgraToPng(result, crop.w, crop.h);
    }

    private static byte[] PngToBgra(byte[] pngBytes, out int width, out int height)
    {
        using var ms = new MemoryStream(pngBytes);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            ms, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        width = frame.PixelWidth;
        height = frame.PixelHeight;
        int stride = width * 4;
        var bgra = new byte[stride * height];
        frame.CopyPixels(bgra, stride, 0);
        return bgra;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_d3dDevicePtr != IntPtr.Zero) { Marshal.Release(_d3dDevicePtr); _d3dDevicePtr = IntPtr.Zero; }
        _sharpDxCtx?.Dispose(); _sharpDxDevice?.Dispose();
    }
    void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ScreenCaptureService)); }
}
