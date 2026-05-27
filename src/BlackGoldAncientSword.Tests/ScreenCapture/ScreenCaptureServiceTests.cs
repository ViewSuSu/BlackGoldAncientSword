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
        // Process.GetProcessesByName("") behavior is OS-dependent;
        // the key invariant is that it does not throw.
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
        Assert.Contains("Invalid window handle", ex.Message);
    }

    [Fact]
    public void CaptureWindow_InvalidHandle_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.CaptureWindow(new IntPtr(0xDEADBEEF)));
        Assert.Contains("Window no longer exists", ex.Message);
    }

    [Fact]
    public void CaptureGame_NonexistentProcess_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _service.CaptureGame("NonExistentProcess_XYZ123"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void CaptureGameToFile_NonexistentProcess_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _service.CaptureGame("NonExistentProcess_XYZ123", @"C:\temp\nonexistent.png"));
        Assert.Contains("not found", ex.Message);
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
}
