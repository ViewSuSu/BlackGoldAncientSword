using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Attributes;
using SharpDX.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace BlackGoldAncientSword.ScreenCapture;

/// <summary>
/// 游戏截屏服务。使用 Windows.Graphics.Capture API 截取 DirectX 游戏窗口画面，
/// 输出 PNG 字节数组或保存到文件。
/// </summary>
[Component(ComponentLifetime.Singleton)]
public class ScreenCaptureService : IScreenCaptureService, IDisposable
{
    private readonly object _deviceLock = new();
    private SharpDX.Direct3D11.Device? _d3dDevice;
    private IDirect3DDevice? _winrtDevice;
    private bool _disposed;

    // ═══════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════

    public bool TryFindGameWindow(string processName, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        var processes = Process.GetProcessesByName(processName);
        if (string.IsNullOrEmpty(processName)) return false;
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
        return CaptureFrameInternal(hwnd);
    }

    public Task<byte[]> CaptureWindowAsync(IntPtr hwnd) =>
        Task.Run(() => CaptureWindow(hwnd));

    public void CaptureWindowToFile(IntPtr hwnd, string filePath)
    {
        var bytes = CaptureWindow(hwnd);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(filePath, bytes);
    }

    public async Task CaptureWindowToFileAsync(IntPtr hwnd, string filePath)
    {
        var bytes = await CaptureWindowAsync(hwnd);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(filePath, bytes);
    }

    public void CaptureGame(string processName, string filePath)
    {
        if (!TryFindGameWindow(processName, out var hwnd))
            throw new InvalidOperationException(
                $"Game process ''{processName}'' not found or has no visible window.");
        CaptureWindowToFile(hwnd, filePath);
    }

    public async Task CaptureGameAsync(string processName, string filePath)
    {
        if (!TryFindGameWindow(processName, out var hwnd))
            throw new InvalidOperationException(
                $"Game process ''{processName}'' not found or has no visible window.");
        await CaptureWindowToFileAsync(hwnd, filePath);
    }

    public byte[] CaptureGame(string processName)
    {
        if (!TryFindGameWindow(processName, out var hwnd))
            throw new InvalidOperationException(
                $"Game process ''{processName}'' not found or has no visible window.");
        return CaptureWindow(hwnd);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Capture pipeline
    // ═══════════════════════════════════════════════════════════════

    private unsafe byte[] CaptureFrameInternal(IntPtr hwnd)
    {
        var device = GetOrCreateD3DDevice();
        var winrtDevice = GetOrCreateWinRTDevice(device);

        GraphicsCaptureItem? captureItem = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;

        try
        {
            captureItem = CreateCaptureItemForWindow(hwnd);

            framePool = Direct3D11CaptureFramePool.Create(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                captureItem.Size);

            session = framePool.CreateCaptureSession(captureItem);

            byte[]? result = null;
            Exception? error = null;
            using var signal = new ManualResetEventSlim(false);

            framePool.FrameArrived += OnFrameArrived;
            session.StartCapture();

            bool ok = signal.Wait(TimeSpan.FromSeconds(5));
            framePool.FrameArrived -= OnFrameArrived;

            if (error != null) throw error;
            if (!ok || result == null)
                throw new TimeoutException(
                    "Timed out waiting for captured frame. " +
                    "Make sure the game window is visible and not minimized.");

            return result;

            void OnFrameArrived(Direct3D11CaptureFramePool pool, object? _)
            {
                try
                {
                    using var frame = pool.TryGetNextFrame();
                    if (frame != null)
                    {
                        result = ConvertFrameToPngBytes(device, frame);
                        signal.Set();
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    signal.Set();
                }
            }
        }
        finally
        {
            session?.Dispose();
            framePool?.Dispose();
        }
    }

    private static unsafe byte[] ConvertFrameToPngBytes(
        SharpDX.Direct3D11.Device device,
        Direct3D11CaptureFrame frame)
    {
        int width = frame.Surface.Description.Width;
        int height = frame.Surface.Description.Height;
        int stride = width * 4;

        // Get the native ID3D11Texture2D from the WinRT IDirect3DSurface
        var surfObj = (IWinRTObject)frame.Surface;
        IntPtr pSurfaceUnknown = surfObj.NativeObject.ThisPtr;

        // Query for IDXGIResource through ID3D11Resource
        using var frameTexture = GetD3D11Texture2DFromSurface(pSurfaceUnknown);

        var desc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None,
        };

        using var staging = new Texture2D(device, desc);
        device.ImmediateContext.CopyResource(frameTexture, staging);

        var box = device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            int srcStride = box.RowPitch;
            byte[] pixelData = new byte[stride * height];

            var src = (byte*)box.DataPointer;
            fixed (byte* dst = pixelData)
            {
                for (int y = 0; y < height; y++)
                {
                    global::System.Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * stride,
                        stride,
                        stride);
                }
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96,
                PixelFormats.Bgra32, null, pixelData, stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            device.ImmediateContext.UnmapSubresource(staging, 0);
        }
    }

    /// <summary>
    /// 从 WinRT IDirect3DSurface 的原始指针获取 SharpDX Texture2D。
    /// 使用 ID3D11Resource COM 接口桥接。
    /// </summary>
    private static unsafe Texture2D GetD3D11Texture2DFromSurface(IntPtr pSurfaceUnknown)
    {
        // ID3D11Resource IID
        Guid iidResource = new("dc8e63f3-d12b-4952-b47b-5e45026a862d");
        IntPtr pResource;
        int hr = Marshal.QueryInterface(pSurfaceUnknown, in iidResource, out pResource);
        Marshal.ThrowExceptionForHR(hr);

        // Wrap as SharpDX Texture2D (takes ownership of the ref)
        var texture = new Texture2D(pResource);
        Marshal.Release(pResource);
        return texture;
    }

    // ═══════════════════════════════════════════════════════════════
    //  D3D11 device
    // ═══════════════════════════════════════════════════════════════

    private SharpDX.Direct3D11.Device GetOrCreateD3DDevice()
    {
        lock (_deviceLock)
        {
            if (_d3dDevice != null && !_d3dDevice.IsDisposed)
                return _d3dDevice;

            _d3dDevice?.Dispose();
            _winrtDevice = null;

            _d3dDevice = new SharpDX.Direct3D11.Device(
                SharpDX.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport);

            return _d3dDevice;
        }
    }

    private IDirect3DDevice GetOrCreateWinRTDevice(SharpDX.Direct3D11.Device d3dDevice)
    {
        lock (_deviceLock)
        {
            if (_winrtDevice != null)
                return _winrtDevice;

            using var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
            IntPtr pDxgi = Marshal.GetIUnknownForObject(dxgiDevice);

            int hr = CreateDirect3D11DeviceFromDXGIDevice(
                pDxgi, out IntPtr pWinrt);
            Marshal.ThrowExceptionForHR(hr);

            _winrtDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(pWinrt);
            Marshal.Release(pWinrt);

            return _winrtDevice;
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    // ═══════════════════════════════════════════════════════════════
    //  GraphicsCaptureItem from HWND
    // ═══════════════════════════════════════════════════════════════

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid, [Out] out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid, [Out] out IntPtr result);
    }

    private static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr hwnd)
    {
        Guid itemIid = typeof(GraphicsCaptureItem).GUID;

        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        int hr = interop.CreateForWindow(hwnd, ref itemIid, out IntPtr pItem);
        Marshal.ThrowExceptionForHR(hr);

        var item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(pItem);
        Marshal.Release(pItem);
        return item;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Win32 helpers
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    // ═══════════════════════════════════════════════════════════════
    //  Disposal
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_deviceLock)
        {
            _winrtDevice = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ScreenCaptureService));
    }
}
