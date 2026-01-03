using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Manages screen capture based on game profile configuration.
/// Supports waiting for windows that aren't open yet and auto-recovery when windows close.
/// </summary>
public class ScreenCaptureManager : IDisposable
{
    private WindowsCaptureService? _captureService;
    private GameProfile? _currentProfile;
    private bool _disposed;
    private IntPtr _currentWindowHandle;
    private DateTime _lastFrameTime = DateTime.MinValue;

    /// <summary>
    /// Gets whether capture is currently running.
    /// </summary>
    public bool IsCapturing => _captureService?.IsCapturing ?? false;

    /// <summary>
    /// Gets whether we're waiting for a window to appear.
    /// True when capture mode is "window" but the window hasn't been found yet.
    /// </summary>
    public bool IsWaitingForWindow { get; private set; }

    /// <summary>
    /// Gets the window title we're waiting for (if any).
    /// </summary>
    public string? WaitingForWindowTitle => IsWaitingForWindow ? _currentProfile?.WindowTitle : null;

    /// <summary>
    /// Gets whether capture mode requires a window (vs monitor capture).
    /// </summary>
    public bool RequiresWindow => _currentProfile?.Capture.Method == "window";

    /// <summary>
    /// Event raised when a new frame is captured.
    /// </summary>
    public event EventHandler<CapturedFrame>? FrameCaptured;

    /// <summary>
    /// Event raised when the target window is found after waiting.
    /// </summary>
    public event EventHandler? WindowFound;

    /// <summary>
    /// Event raised when the target window is lost during capture.
    /// </summary>
    public event EventHandler? WindowLost;

    /// <summary>
    /// Initializes capture for the specified game profile.
    /// For window capture mode, this will succeed even if the window isn't found yet.
    /// Use IsWaitingForWindow to check if the window needs to be found before capture can start.
    /// </summary>
    /// <param name="profile">The game profile with capture settings.</param>
    /// <returns>True if initialization succeeded (always true for valid profiles).</returns>
    public bool Initialize(GameProfile profile)
    {
        _currentProfile = profile;
        _currentWindowHandle = IntPtr.Zero;
        IsWaitingForWindow = false;
        _lastFrameTime = DateTime.MinValue;

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
                    _currentWindowHandle = windowHandle;
                    _captureService.InitializeForWindow(windowHandle);
                    Logger.Log($"ScreenCaptureManager: Window found immediately: {profile.WindowTitle}");
                    return true;
                }
                else
                {
                    // Window not found - enter waiting state instead of failing
                    IsWaitingForWindow = true;
                    Logger.Log($"ScreenCaptureManager: Window not found, waiting for: {profile.WindowTitle}");
                    return true; // Success - we're just waiting
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
    /// If the window is found and we were waiting, fires WindowFound event.
    /// </summary>
    /// <returns>True if window was found (or not needed for monitor capture).</returns>
    public bool TryFindWindow()
    {
        if (_currentProfile == null || _captureService == null)
            return false;

        if (_currentProfile.Capture.Method != "window")
            return true;

        var windowHandle = WindowFinder.FindWindowByTitle(_currentProfile.WindowTitle);
        if (windowHandle != IntPtr.Zero)
        {
            var wasWaiting = IsWaitingForWindow;
            _currentWindowHandle = windowHandle;
            IsWaitingForWindow = false;
            _captureService.InitializeForWindow(windowHandle);

            if (wasWaiting)
            {
                Logger.Log($"ScreenCaptureManager: Window found after waiting: {_currentProfile.WindowTitle}");
                WindowFound?.Invoke(this, EventArgs.Empty);
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the target window is still valid and visible.
    /// If the window was lost, enters waiting state and fires WindowLost event.
    /// </summary>
    /// <returns>True if window is still valid, false if lost.</returns>
    public bool CheckWindowStillValid()
    {
        if (_currentProfile == null)
            return false;

        // Monitor capture doesn't need window validation
        if (_currentProfile.Capture.Method != "window")
            return true;

        // If we're already waiting, window is not valid
        if (IsWaitingForWindow)
            return false;

        // Check if our captured window handle is still valid
        if (_currentWindowHandle == IntPtr.Zero)
            return false;

        // Check if window still exists and is visible
        if (!Native.User32.IsWindow(_currentWindowHandle) || !Native.User32.IsWindowVisible(_currentWindowHandle))
        {
            Logger.Log($"ScreenCaptureManager: Window lost: {_currentProfile.WindowTitle}");
            HandleWindowLost();
            return false;
        }

        // Also verify by title in case the same handle got reused for a different window
        var currentTitle = WindowFinder.GetWindowTitle(_currentWindowHandle);
        if (string.IsNullOrEmpty(currentTitle) ||
            !currentTitle.Contains(_currentProfile.WindowTitle, StringComparison.OrdinalIgnoreCase))
        {
            // Window title changed or window closed - try to find it again
            var newHandle = WindowFinder.FindWindowByTitle(_currentProfile.WindowTitle);
            if (newHandle == IntPtr.Zero)
            {
                Logger.Log($"ScreenCaptureManager: Window title changed or closed: {_currentProfile.WindowTitle}");
                HandleWindowLost();
                return false;
            }
            else if (newHandle != _currentWindowHandle)
            {
                // Window moved to a new handle (rare but possible)
                Logger.Log($"ScreenCaptureManager: Window handle changed, reinitializing");
                _currentWindowHandle = newHandle;
                _captureService?.StopCapture();
                _captureService?.InitializeForWindow(newHandle);
            }
        }

        return true;
    }

    /// <summary>
    /// Handles the window being lost - stops capture and enters waiting state.
    /// </summary>
    private void HandleWindowLost()
    {
        _currentWindowHandle = IntPtr.Zero;
        IsWaitingForWindow = true;

        // Stop the current capture session
        if (_captureService != null)
        {
            _captureService.FrameCaptured -= OnFrameCaptured;
            _captureService.StopCapture();
        }

        WindowLost?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the last frame time. Call this when a frame is received to track capture health.
    /// </summary>
    public void UpdateLastFrameTime()
    {
        _lastFrameTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if frames have stopped arriving (potential window loss).
    /// </summary>
    /// <param name="timeoutSeconds">How long without frames before considering capture stalled.</param>
    /// <returns>True if capture appears stalled.</returns>
    public bool IsCaptureStalled(double timeoutSeconds = 2.0)
    {
        if (!IsCapturing || _lastFrameTime == DateTime.MinValue)
            return false;

        return (DateTime.UtcNow - _lastFrameTime).TotalSeconds > timeoutSeconds;
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
