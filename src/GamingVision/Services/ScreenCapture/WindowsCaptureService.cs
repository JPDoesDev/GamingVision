using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Screen capture service using Windows.Graphics.Capture API.
/// Falls back to GDI capture if WGC is not available.
/// </summary>
public class WindowsCaptureService : IScreenCaptureService
{
    private readonly GdiCaptureService _fallbackService;
    private bool _disposed;

    public bool IsCapturing => _fallbackService.IsCapturing;

    public event EventHandler<CapturedFrame>? FrameCaptured
    {
        add => _fallbackService.FrameCaptured += value;
        remove => _fallbackService.FrameCaptured -= value;
    }

    public WindowsCaptureService()
    {
        _fallbackService = new GdiCaptureService();
    }

    /// <summary>
    /// Checks if Windows.Graphics.Capture is supported on this system.
    /// </summary>
    public static bool IsWgcSupported()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initializes capture for a specific window by title.
    /// </summary>
    public bool InitializeForWindowTitle(string windowTitle)
    {
        // For now, use GDI fallback
        // TODO: Implement WGC when needed for better performance
        return _fallbackService.InitializeForWindowTitle(windowTitle);
    }

    /// <summary>
    /// Initializes capture for a specific window by handle.
    /// </summary>
    public void InitializeForWindow(IntPtr windowHandle)
    {
        _fallbackService.InitializeForWindow(windowHandle);
    }

    /// <summary>
    /// Initializes capture for a specific monitor.
    /// </summary>
    public void InitializeForMonitor(int monitorIndex)
    {
        _fallbackService.InitializeForMonitor(monitorIndex);
    }

    /// <summary>
    /// Sets the capture interval in milliseconds.
    /// </summary>
    public void SetCaptureInterval(int intervalMs)
    {
        _fallbackService.SetCaptureInterval(intervalMs);
    }

    public Task<bool> StartCaptureAsync()
    {
        return _fallbackService.StartCaptureAsync();
    }

    public void StopCapture()
    {
        _fallbackService.StopCapture();
    }

    public CapturedFrame? GetLatestFrame()
    {
        return _fallbackService.GetLatestFrame();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _fallbackService.Dispose();
        GC.SuppressFinalize(this);
    }
}
