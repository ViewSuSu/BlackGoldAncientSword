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

    public static (byte[] data, int w, int h)? Capture(IntPtr hwnd, int expectedW, int expectedH, (int x, int y, int w, int h) crop)
    {
        int hr = wgc_capture_window(hwnd, out int fw, out int fh, out IntPtr pixelPtr);
        if (hr != 0 || pixelPtr == IntPtr.Zero)
        {
            Console.WriteLine($"[NativeWGC] wgc_capture_window failed: 0x{hr:X8}");
            return null;
        }

        try
        {
            int pixelCount = fw * fh * 4;
            byte[] fullData = new byte[pixelCount];
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

            Console.WriteLine($"[NativeWGC] WGC {fw}x{fh} (window {expectedW}x{expectedH}), cropped {sw}x{sh}");
            return (result, sw, sh);
        }
        finally
        {
            wgc_free_buffer(pixelPtr);
        }
    }

    public static void Init()
    {
        int hr = wgc_capture_init();
        if (hr != 0) Console.WriteLine($"[NativeWGC] init failed: 0x{hr:X8}");
        else Console.WriteLine("[NativeWGC] initialized");
    }

    public static void Cleanup() { wgc_capture_cleanup(); }
}
