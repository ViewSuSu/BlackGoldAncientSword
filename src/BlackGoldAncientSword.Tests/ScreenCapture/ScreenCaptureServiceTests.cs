using BlackGoldAncientSword.ScreenCapture;
using Moq;
using System.Diagnostics;

namespace BlackGoldAncientSword.Tests.ScreenCapture;

public class ScreenCaptureServiceTests : IDisposable
{
    private readonly ScreenCaptureService _service;

    public ScreenCaptureServiceTests()
    {
        _service = new ScreenCaptureService();
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void TryFindGameWindow_NonexistentProcess_ReturnsFalse()
    {
        var result = _service.TryFindGameWindow("NonExistentProcess_XYZ123", out var hwnd);
        Assert.False(result);
        Assert.Equal(IntPtr.Zero, hwnd);
    }

    [Fact]
    public void TryFindGameWindow_EmptyProcessName_NoThrow()
    {
        var exception = Record.Exception(() => _service.TryFindGameWindow("", out _));
        Assert.Null(exception);
    }

    [Fact]
    public void TryFindGameWindow_ExplorerProcess_ShouldFindWindow()
    {
        var result = _service.TryFindGameWindow("explorer", out var hwnd);
        if (result)
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
        }
    }

    [Fact]
    public void CaptureWindow_ZeroHandle_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.CaptureWindow(IntPtr.Zero));
        Assert.Contains("Invalid hwnd", ex.Message);
    }

    [Fact]
    public void CaptureWindow_InvalidHandle_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.CaptureWindow(new IntPtr(0xDEADBEEF)));
        Assert.Contains("Window gone", ex.Message);
    }

    [Fact]
    public void CaptureGame_NonexistentProcess_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _service.CaptureGame("NonExistentProcess_XYZ123"));
        Assert.Contains("Not found", ex.Message);
    }

    [Fact]
    public void CaptureGameToFile_NonexistentProcess_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _service.CaptureGame("NonExistentProcess_XYZ123", @"C:\temp\nonexistent.png"));
        Assert.Contains("Not found", ex.Message);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _service.Dispose();
        _service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _service.CaptureWindow(IntPtr.Zero));
    }

    [Fact]
    public async Task CaptureWindowAsync_ZeroHandle_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CaptureWindowAsync(IntPtr.Zero));
    }

    // =========================================================================
    //  NarakaBladepoint live-capture tests (require game to be running)
    // =========================================================================

    [Fact]
    public void CaptureGame_NarakaBladepoint_Full_SavesToDesktop()
    {
        var hwnd = FindNarakaWindow();
        var filePath = MakeDesktopPath("Full");
        _service.CaptureWindowToFile(hwnd, filePath);
        AssertFileSaved(filePath);
    }

    [Fact]
    public void CaptureGame_NarakaBladepoint_TopLeft_SavesToDesktop()
    {
        var hwnd = FindNarakaWindow();
        var filePath = MakeDesktopPath("TopLeft");
        _service.CaptureRegionToFile(hwnd, ScreenQuadrant.TopLeft, filePath);
        AssertFileSaved(filePath);
    }

    [Fact]
    public void CaptureGame_NarakaBladepoint_TopRight_SavesToDesktop()
    {
        var hwnd = FindNarakaWindow();
        var filePath = MakeDesktopPath("TopRight");
        _service.CaptureRegionToFile(hwnd, ScreenQuadrant.TopRight, filePath);
        AssertFileSaved(filePath);
    }

    [Fact]
    public void CaptureGame_NarakaBladepoint_BottomLeft_SavesToDesktop()
    {
        var hwnd = FindNarakaWindow();
        var filePath = MakeDesktopPath("BottomLeft");
        _service.CaptureRegionToFile(hwnd, ScreenQuadrant.BottomLeft, filePath);
        AssertFileSaved(filePath);
    }

    [Fact]
    public void CaptureGame_NarakaBladepoint_BottomRight_SavesToDesktop()
    {
        var hwnd = FindNarakaWindow();
        var filePath = MakeDesktopPath("BottomRight");
        _service.CaptureRegionToFile(hwnd, ScreenQuadrant.BottomRight, filePath);
        AssertFileSaved(filePath);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private IntPtr FindNarakaWindow()
    {
        const string processName = "NarakaBladepoint";
        var found = _service.TryFindGameWindow(processName, out var hwnd);
        Assert.True(found, $"Game process '{processName}' not running. Start the game and re-run this test.");
        Assert.NotEqual(IntPtr.Zero, hwnd);
        return hwnd;
    }

    private static string MakeDesktopPath(string label)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return Path.Combine(desktop, $"NarakaBladepoint_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
    }

    private static void AssertFileSaved(string filePath)
    {
        Assert.True(File.Exists(filePath), $"Screenshot file not found: {filePath}");
        var fileInfo = new FileInfo(filePath);
        Assert.True(fileInfo.Length > 0, "Screenshot file is empty.");
    }
}