using System.Runtime.InteropServices;

namespace BlackGoldAncientSword.ScreenCapture;

internal static class WgcInterop
{
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
    static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string s, int len, out IntPtr hstr);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    static extern int WindowsDeleteString(IntPtr hstr);

    static readonly Guid IID_IActivationFactory = new(0x00000035, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
    static readonly Guid IID_IGraphicsCaptureItemInterop = new(0x3628E81B, 0x3CAC, 0x4C60, 0x92, 0x07, 0x6A, 0x5D, 0x7B, 0x3C, 0x2C, 0x0C);
    static readonly Guid IID_IDirect3D11CaptureFramePoolStatics = new(0xEF106DE2, 0x6CCF, 0x539B, 0x9B, 0xA6, 0x96, 0xDA, 0x5F, 0x17, 0x4A, 0xEA);
    static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new(0xA9B3D012, 0x3DF2, 0x4EE3, 0xB8, 0xD1, 0x86, 0x95, 0xF4, 0x57, 0xD3, 0xC1);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D1(IntPtr self, IntPtr device, int fmt, int nBuf, SizeN sz, out IntPtr pool);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D2(IntPtr self, IntPtr device, IntPtr item, out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D3(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D4(IntPtr self, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D5(IntPtr self, out IntPtr surface);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D6(IntPtr self, [MarshalAs(UnmanagedType.LPStruct)] ref Guid iid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int D7(IntPtr self, IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] ref Guid riid, out IntPtr result);

    [StructLayout(LayoutKind.Sequential)] struct SizeN { public int Width; public int Height; }

    public static IntPtr CreateCaptureItemForWindow(IntPtr hwnd)
    {
        var name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = WindowsCreateString(name, name.Length, out var hs);
        if (hr != 0) throw new InvalidOperationException($"WindowsCreateString: 0x{hr:X8}");
        try
        {
            // Get activation factory, then QI for interop
            hr = RoGetActivationFactory(hs, IID_IActivationFactory, out var fac);
            if (hr != 0)
                throw new NotSupportedException($"RoGetActivationFactory: 0x{hr:X8}. Windows.Graphics.Capture may not be available.");

            var interopIid = IID_IGraphicsCaptureItemInterop;
            hr = Marshal.QueryInterface(fac, ref interopIid, out var interop);
            Marshal.Release(fac);

            if (hr != 0)
                throw new NotSupportedException($"IGraphicsCaptureItemInterop not available on this system (0x{hr:X8}). " +
                    "This interface is required to create capture items from HWND. " +
                    "It may require Windows 10 1903+ or a specific Windows SDK.");

            try
            {
                var vt = Vt(interop, 3);
                var del = Marshal.GetDelegateForFunctionPointer<D7>(vt);
                var itemIid = new Guid(0x79C3F95B, 0x31F7, 0x4EC2, 0xA4, 0x64, 0x63, 0x2E, 0xF5, 0xD3, 0x07, 0x60);
                hr = del(interop, hwnd, ref itemIid, out var item);
                if (hr != 0) throw new InvalidOperationException($"CreateForWindow: 0x{hr:X8}");
                Console.WriteLine($"[WGC] Item: 0x{item:X}");
                return item;
            }
            finally { Marshal.Release(interop); }
        }
        finally { WindowsDeleteString(hs); }
    }

    public static IntPtr CreateFreeThreadedFramePool(IntPtr dev, int w, int h)
    {
        var name = "Windows.Graphics.Capture.Direct3D11CaptureFramePool";
        int hr = WindowsCreateString(name, name.Length, out var hs);
        if (hr != 0) throw new InvalidOperationException($"WindowsCreateString: 0x{hr:X8}");
        try
        {
            hr = RoGetActivationFactory(hs, IID_IDirect3D11CaptureFramePoolStatics, out var fac);
            if (hr != 0) throw new InvalidOperationException($"RoGetActivationFactory(Pool): 0x{hr:X8}");
            try
            {
                var vt = Vt(fac, 7);
                var del = Marshal.GetDelegateForFunctionPointer<D1>(vt);
                var sz = new SizeN { Width = w, Height = h };
                hr = del(fac, dev, 87, 1, sz, out var pool);
                if (hr != 0) throw new InvalidOperationException($"CreateFreeThreaded: 0x{hr:X8}");
                Console.WriteLine($"[WGC] Pool: 0x{pool:X}");
                return pool;
            }
            finally { Marshal.Release(fac); }
        }
        finally { WindowsDeleteString(hs); }
    }

    public static IntPtr CreateCaptureSession(IntPtr pool, IntPtr dev, IntPtr item)
    {
        var vt = Vt(pool, 6);
        var del = Marshal.GetDelegateForFunctionPointer<D2>(vt);
        int hr = del(pool, dev, item, out var sess);
        if (hr != 0) throw new InvalidOperationException($"CreateCaptureSession: 0x{hr:X8}");
        Console.WriteLine($"[WGC] Session: 0x{sess:X}");
        return sess;
    }

    public static void StartCapture(IntPtr session)
    {
        var vt = Vt(session, 6);
        var del = Marshal.GetDelegateForFunctionPointer<D3>(vt);
        int hr = del(session);
        if (hr != 0) throw new InvalidOperationException($"StartCapture: 0x{hr:X8}");
        Console.WriteLine($"[WGC] Capture started");
    }

    public static IntPtr WaitForFrame(IntPtr pool, int timeoutMs)
    {
        var vt = Vt(pool, 10);
        var del = Marshal.GetDelegateForFunctionPointer<D4>(vt);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            int hr = del(pool, out var frame);
            if (hr == 0 && frame != IntPtr.Zero)
            {
                Console.WriteLine($"[WGC] Frame 0x{frame:X} @ {sw.ElapsedMilliseconds}ms");
                return frame;
            }
            Thread.Sleep(16);
        }
        throw new TimeoutException($"WGC frame timeout ({timeoutMs}ms)");
    }

    public static IntPtr GetFrameSurface(IntPtr frame)
    {
        var vt = Vt(frame, 6);
        var del = Marshal.GetDelegateForFunctionPointer<D5>(vt);
        int hr = del(frame, out var surface);
        if (hr != 0) throw new InvalidOperationException($"GetSurface: 0x{hr:X8}");
        return surface;
    }

    public static IntPtr GetDxgiInterface(IntPtr surface, Guid dxgiIid)
    {
        var accessIid = IID_IDirect3DDxgiInterfaceAccess;
        int hr = Marshal.QueryInterface(surface, ref accessIid, out var access);
        if (hr != 0) throw new InvalidOperationException($"QI(DxgiAccess): 0x{hr:X8}");
        try
        {
            var vt = Vt(access, 3);
            var del = Marshal.GetDelegateForFunctionPointer<D6>(vt);
            hr = del(access, ref dxgiIid, out var dxgi);
            if (hr != 0) throw new InvalidOperationException($"GetInterface: 0x{hr:X8}");
            return dxgi;
        }
        finally { Marshal.Release(access); }
    }

    static IntPtr Vt(IntPtr p, int slot)
    {
        var vtable = Marshal.ReadIntPtr(p);
        return Marshal.ReadIntPtr(vtable + slot * IntPtr.Size);
    }
}
