using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Manages screen capture based on game profile configuration.
/// </summary>
public class ScreenCaptureManager : IDisposable
{
    private WindowsCaptureService? _captureService;
    private GameProfile? _currentProfile;
    private bool _disposed;

    /// <summary>
    /// Gets whether capture is currently running.
    /// </summary>
    public bool IsCapturing => _captureService?.IsCapturing ?? false;

    /// <summary>
    /// Event raised when a new frame is captured.
    /// </summary>
    public event EventHandler<CapturedFrame>? FrameCaptured;

    /// <summary>
    /// Initializes capture for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile with capture settings.</param>
    /// <returns>True if initialization succeeded.</returns>
    public bool Initialize(GameProfile profile)
    {
        _currentProfile = profile;
        _captureService?.Dispose();
        _captureService = new WindowsCaptureService();

        // Set capture interval for 30 FPS target (33ms between frames)
        // This enables smooth overlay updates for object tracking
        _captureService.SetCaptureInterval(33);

        if (profile.Capture.Method == "window")
        {
            // Try to find the game window
            if (!string.IsNullOrEmpty(profile.WindowTitle))
            {
                var windowHandle = WindowFinder.FindWindowByTitle(profile.WindowTitle);
                if (windowHandle != IntPtr.Zero)
                {
                    _captureService.InitializeForWindow(windowHandle);
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Window not found: {profile.WindowTitle}");
                    return false;
                }
            }
        }
        else
        {
            // Fullscreen/monitor capture
            _captureService.InitializeForMonitor(profile.Capture.MonitorIndex);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts the capture process.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        Logger.Log("ScreenCaptureManager: StartAsync called");
        if (_captureService == null)
        {
            Logger.Warn("ScreenCaptureManager: StartAsync - capture service is null");
            return false;
        }

        _captureService.FrameCaptured += OnFrameCaptured;
        Logger.Log("ScreenCaptureManager: Starting capture");
        var result = await _captureService.StartCaptureAsync();
        Logger.Log($"ScreenCaptureManager: StartCaptureAsync returned {result}");
        return result;
    }

    /// <summary>
    /// Stops the capture process.
    /// </summary>
    public void Stop()
    {
        Logger.Log("ScreenCaptureManager: Stop called");
        if (_captureService != null)
        {
            _captureService.FrameCaptured -= OnFrameCaptured;
            _captureService.StopCapture();
            Logger.Log("ScreenCaptureManager: Capture stopped");
        }
    }

    /// <summary>
    /// Gets the most recent captured frame.
    /// </summary>
    public CapturedFrame? GetLatestFrame()
    {
        return _captureService?.GetLatestFrame();
    }

    /// <summary>
    /// Captures a single frame synchronously using PrintWindow (bypasses overlays).
    /// Use this for training data capture to avoid capturing bounding box overlays.
    /// </summary>
    public CapturedFrame? CaptureFrame()
    {
        return _captureService?.CaptureFrame();
    }

    /// <summary>
    /// Tries to find the game window again (useful if the game was launched after initialization).
    /// </summary>
    public bool TryFindWindow()
    {
        if (_currentProfile == null || _captureService == null)
            return false;

        if (_currentProfile.Capture.Method != "window")
            return true;

        var windowHandle = WindowFinder.FindWindowByTitle(_currentProfile.WindowTitle);
        if (windowHandle != IntPtr.Zero)
        {
            _captureService.InitializeForWindow(windowHandle);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a list of available monitors.
    /// </summary>
    public static List<MonitorInfo> GetAvailableMonitors()
    {
        return GdiCaptureService.GetMonitors();
    }

    /// <summary>
    /// Gets a list of visible windows.
    /// </summary>
    public static List<WindowInfo> GetVisibleWindows()
    {
        return WindowFinder.GetVisibleWindows();
    }

    private void OnFrameCaptured(object? sender, CapturedFrame e)
    {
        FrameCaptured?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _captureService?.Dispose();
        _captureService = null;

        GC.SuppressFinalize(this);
    }
}
