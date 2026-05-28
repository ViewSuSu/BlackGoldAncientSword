#define _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <stdint.h>
#include <stdio.h>
#include <roapi.h>
#include <winstring.h>
#include <wrl/client.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

using Microsoft::WRL::ComPtr;
namespace wc = winrt::Windows::Graphics::Capture;
namespace wdx = winrt::Windows::Graphics::DirectX;
namespace wd3d = winrt::Windows::Graphics::DirectX::Direct3D11;

static ComPtr<ID3D11Device>        g_d3dDevice;
static ComPtr<ID3D11DeviceContext> g_d3dContext;

extern "C" {

__declspec(dllexport) int __stdcall wgc_capture_init(void)
{
    if (g_d3dDevice) return 0;
    D3D_FEATURE_LEVEL fl;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
        &g_d3dDevice, &fl, &g_d3dContext);
    return SUCCEEDED(hr) ? 0 : (int)hr;
}

__declspec(dllexport) int __stdcall wgc_capture_window(HWND hwnd, int* wOut, int* hOut, unsigned char** pixOut)
{
    *pixOut = nullptr; *wOut = 0; *hOut = 0;
    if (!g_d3dDevice) { int r = wgc_capture_init(); if (r) return r; }

    RECT wr;
    if (!GetWindowRect(hwnd, &wr)) return HRESULT_FROM_WIN32(GetLastError());
    int W = wr.right - wr.left, H = wr.bottom - wr.top;
    if (W <= 0 || H <= 0) return E_INVALIDARG;

    try {
        // ── 1. Create WinRT IDirect3DDevice ──
        ComPtr<IDXGIDevice> dxgiDev;
        HRESULT hr = g_d3dDevice->QueryInterface(IID_PPV_ARGS(&dxgiDev));
        if (FAILED(hr)) return hr;

        winrt::com_ptr<::IInspectable> d3dInsp;
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDev.Get(), d3dInsp.put());
        if (FAILED(hr)) { fprintf(stderr, "[wgc] CreateD3DDevice failed: 0x%08X\n", (unsigned)hr); return hr; }

        auto d3dDev = d3dInsp.as<wd3d::IDirect3DDevice>();
        fprintf(stderr, "[wgc] WinRT D3D device OK\n");

        // ── 2. Create GraphicsCaptureItem from HWND ──
        auto interopFactory = winrt::get_activation_factory<wc::GraphicsCaptureItem>();
        fprintf(stderr, "[wgc] Activation factory OK\n");

        auto interop = interopFactory.as<IGraphicsCaptureItemInterop>();
        fprintf(stderr, "[wgc] IGraphicsCaptureItemInterop OK\n");

        wc::GraphicsCaptureItem item{ nullptr };
        hr = interop->CreateForWindow(hwnd, winrt::guid_of<wc::GraphicsCaptureItem>(), winrt::put_abi(item));
        if (FAILED(hr)) { fprintf(stderr, "[wgc] CreateForWindow: 0x%08X\n", (unsigned)hr); return hr; }
        fprintf(stderr, "[wgc] Item: %dx%d\n", item.Size().Width, item.Size().Height);

        // ── 3. Frame pool ──
        auto pool = wc::Direct3D11CaptureFramePool::CreateFreeThreaded(
            d3dDev, wdx::DirectXPixelFormat::B8G8R8A8UIntNormalized, 1, item.Size());
        fprintf(stderr, "[wgc] Pool OK\n");

        // ── 4. Session & start ──
        auto session = pool.CreateCaptureSession(item);
        session.StartCapture();
        fprintf(stderr, "[wgc] Capture started\n");

        // ── 5. Wait for frame ──
        wc::Direct3D11CaptureFrame frame{ nullptr };
        for (int i = 0; i < 300; i++) {
            frame = pool.TryGetNextFrame();
            if (frame) break;
            Sleep(16);
        }
        if (!frame) { fprintf(stderr, "[wgc] Frame timeout\n"); return HRESULT_FROM_WIN32(ERROR_TIMEOUT); }
        fprintf(stderr, "[wgc] Frame OK, size=%dx%d\n", frame.ContentSize().Width, frame.ContentSize().Height);

        // ── 6. Get DXGI surface ──
        auto surface = frame.Surface();
        auto surfaceUnk = surface.as<::IUnknown>();
        winrt::com_ptr<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess> dxgiAccess;
        hr = surfaceUnk->QueryInterface(winrt::guid_of<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>(), dxgiAccess.put_void());
        if (FAILED(hr)) { fprintf(stderr, "[wgc] QI(DxgiAccess): 0x%08X\n", (unsigned)hr); return hr; }

        winrt::com_ptr<IDXGISurface> dxgiSurface;
        hr = dxgiAccess->GetInterface(__uuidof(IDXGISurface), dxgiSurface.put_void());
        if (FAILED(hr)) { fprintf(stderr, "[wgc] GetInterface: 0x%08X\n", (unsigned)hr); return hr; }

        // ── 7. Read pixels ──
        DXGI_SURFACE_DESC desc;
        dxgiSurface->GetDesc(&desc);

        D3D11_TEXTURE2D_DESC sd = {};
        sd.Width = desc.Width; sd.Height = desc.Height; sd.MipLevels = 1; sd.ArraySize = 1;
        sd.Format = desc.Format; sd.SampleDesc.Count = 1;
        sd.Usage = D3D11_USAGE_STAGING; sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

        ComPtr<ID3D11Texture2D> stagingTex;
        hr = g_d3dDevice->CreateTexture2D(&sd, nullptr, &stagingTex);
        if (FAILED(hr)) return hr;

        ComPtr<ID3D11Texture2D> surfTex;
        hr = dxgiSurface->QueryInterface(IID_PPV_ARGS(&surfTex));
        if (FAILED(hr)) return hr;

        g_d3dContext->CopyResource(stagingTex.Get(), surfTex.Get());

        D3D11_MAPPED_SUBRESOURCE map = {};
        hr = g_d3dContext->Map(stagingTex.Get(), 0, D3D11_MAP_READ, 0, &map);
        if (FAILED(hr)) return hr;

        int bytes = desc.Width * desc.Height * 4;
        auto* pixels = (unsigned char*)HeapAlloc(GetProcessHeap(), 0, bytes);
        if (!pixels) { g_d3dContext->Unmap(stagingTex.Get(), 0); return E_OUTOFMEMORY; }

        if ((int)map.RowPitch == desc.Width * 4)
            memcpy(pixels, map.pData, bytes);
        else
            for (UINT y = 0; y < desc.Height; y++)
                memcpy(pixels + y * desc.Width * 4, (unsigned char*)map.pData + y * map.RowPitch, desc.Width * 4);

        g_d3dContext->Unmap(stagingTex.Get(), 0);
        fprintf(stderr, "[wgc] Pixels: %dx%d\n", desc.Width, desc.Height);

        *pixOut = pixels; *wOut = desc.Width; *hOut = desc.Height;
        return 0;
    }
    catch (winrt::hresult_error const& ex) {
        fprintf(stderr, "[wgc] WinRT error 0x%08X: %ls\n", (unsigned)ex.code(), ex.message().c_str());
        return ex.code();
    }
    catch (std::exception const& ex) {
        fprintf(stderr, "[wgc] Exception: %s\n", ex.what());
        return E_FAIL;
    }
    catch (...) {
        fprintf(stderr, "[wgc] Unknown exception\n");
        return E_FAIL;
    }
}

__declspec(dllexport) void __stdcall wgc_free_buffer(unsigned char* buf) { if (buf) HeapFree(GetProcessHeap(), 0, buf); }
__declspec(dllexport) void __stdcall wgc_capture_cleanup(void) { g_d3dContext.Reset(); g_d3dDevice.Reset(); }

} // extern "C"

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_DETACH) { g_d3dContext.Reset(); g_d3dDevice.Reset(); }
    return TRUE;
}


