using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlackGoldAncientSword.ScreenCapture;

internal static class NativeWgc
{
    private const string DllName = "wgc_capture.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int wgc_capture_init();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int wgc_capture_window(
        IntPtr hwnd, out int width, out int height, out IntPtr pixels);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void wgc_free_buffer(IntPtr buffer);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void wgc_capture_cleanup();

    /// <summary>
    /// 走 Native WGC 抓一帧,内部用 <see cref="ArrayPool{T}"/> 复用 ~8MB 的全帧缓冲,
    /// 避免每次截图触发 LOH 分配。最终返回的裁剪后小数组仍是 new(归调用方持有)。
    /// </summary>
    public static (byte[] data, int w, int h)? Capture(IntPtr hwnd, int expectedW, int expectedH, (int x, int y, int w, int h) crop)
    {
        int hr = wgc_capture_window(hwnd, out int fw, out int fh, out IntPtr pixelPtr);
        if (hr != 0 || pixelPtr == IntPtr.Zero)
        {
            Debug.WriteLine($"[NativeWGC] wgc_capture_window failed: 0x{hr:X8}");
            return null;
        }

        int pixelCount = fw * fh * 4;
        var pool = ArrayPool<byte>.Shared;
        // Rent 给的 buffer 可能比 pixelCount 大,后续切片只用前 pixelCount 字节即可。
        byte[] fullData = pool.Rent(pixelCount);
        try
        {
            Marshal.Copy(pixelPtr, fullData, 0, pixelCount);

            double scaleX = (double)fw / expectedW;
            double scaleY = (double)fh / expectedH;

            int sx = (int)(crop.x * scaleX);
            int sy = (int)(crop.y * scaleY);
            int sw = (int)(crop.w * scaleX);
            int sh = (int)(crop.h * scaleY);

            if (sx < 0) sx = 0; if (sy < 0) sy = 0;
            if (sx + sw > fw) sw = fw - sx;
            if (sy + sh > fh) sh = fh - sy;
            if (sw <= 0 || sh <= 0) return null;

            var result = new byte[sw * sh * 4];
            int fs = fw * 4, cs = sw * 4;
            for (int y = 0; y < sh; y++)
                Array.Copy(fullData, (sy + y) * fs + sx * 4, result, y * cs, cs);

            Debug.WriteLine($"[NativeWGC] WGC {fw}x{fh} (window {expectedW}x{expectedH}), cropped {sw}x{sh}");
            return (result, sw, sh);
        }
        finally
        {
            // 归还 buffer 前不需要 clearArray:像素是公开图像,无敏感数据,且下次 Rent 会被覆盖。
            pool.Return(fullData);
            wgc_free_buffer(pixelPtr);
        }
    }

    /// <summary>
    /// 抓完整窗口 BGRA 像素,不做裁剪;为 OCR 路径准备(后续 region 裁剪在上层做,
    /// 避免"Native 内裁一次 + 上层再裁一次"的双重拷贝)。
    /// <para>
    /// 返回数组用 ArrayPool 租用,调用方用完必须调 <see cref="ReturnRawBuffer"/> 归还,
    /// 否则 pool 会持续扩容。配套的 <c>actualLength</c> 是有效像素字节数(数组长度可能更大)。
    /// </para>
    /// </summary>
    public static bool CaptureRaw(IntPtr hwnd, out byte[] pooledBuffer, out int actualLength, out int width, out int height)
    {
        pooledBuffer = Array.Empty<byte>();
        actualLength = 0;
        width = 0;
        height = 0;

        int hr = wgc_capture_window(hwnd, out int fw, out int fh, out IntPtr pixelPtr);
        if (hr != 0 || pixelPtr == IntPtr.Zero)
        {
            Debug.WriteLine($"[NativeWGC] wgc_capture_window failed: 0x{hr:X8}");
            return false;
        }

        try
        {
            int pixelCount = fw * fh * 4;
            pooledBuffer = ArrayPool<byte>.Shared.Rent(pixelCount);
            Marshal.Copy(pixelPtr, pooledBuffer, 0, pixelCount);
            actualLength = pixelCount;
            width = fw;
            height = fh;
            return true;
        }
        finally
        {
            wgc_free_buffer(pixelPtr);
        }
    }

    public static void ReturnRawBuffer(byte[] buffer)
    {
        if (buffer != null && buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    public static void Init()
    {
        int hr = wgc_capture_init();
        if (hr != 0) Debug.WriteLine($"[NativeWGC] init failed: 0x{hr:X8}");
        else Debug.WriteLine("[NativeWGC] initialized");
    }

    public static void Cleanup() { wgc_capture_cleanup(); }
}
