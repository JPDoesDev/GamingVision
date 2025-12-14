using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Screen capture service using GDI+ BitBlt.
/// Simple and compatible fallback for screen capture.
/// </summary>
public class GdiCaptureService : IScreenCaptureService
{
    private IntPtr _windowHandle;
    private int _monitorIndex;
    private bool _captureWindow;
    private CapturedFrame? _latestFrame;
    private readonly object _frameLock = new();
    private CancellationTokenSource? _captureLoopCts;
    private Task? _captureLoopTask;
    private bool _disposed;
    private int _captureIntervalMs = 100; // ~10 FPS

    public bool IsCapturing { get; private set; }

    public event EventHandler<CapturedFrame>? FrameCaptured;

    /// <summary>
    /// Initializes capture for a specific window by handle.
    /// </summary>
    public void InitializeForWindow(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _captureWindow = true;
    }

    /// <summary>
    /// Initializes capture for a window by title (partial match).
    /// </summary>
    public bool InitializeForWindowTitle(string windowTitle)
    {
        _windowHandle = WindowFinder.FindWindowByTitle(windowTitle);
        if (_windowHandle == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine($"Window not found: {windowTitle}");
            return false;
        }
        _captureWindow = true;
        return true;
    }

    /// <summary>
    /// Initializes capture for a specific monitor.
    /// </summary>
    public void InitializeForMonitor(int monitorIndex)
    {
        _monitorIndex = monitorIndex;
        _captureWindow = false;
    }

    /// <summary>
    /// Sets the capture interval in milliseconds.
    /// </summary>
    public void SetCaptureInterval(int intervalMs)
    {
        _captureIntervalMs = Math.Max(50, intervalMs); // Minimum 50ms (~20 FPS max)
    }

    public Task<bool> StartCaptureAsync()
    {
        if (IsCapturing)
            return Task.FromResult(true);

        _captureLoopCts = new CancellationTokenSource();
        _captureLoopTask = Task.Run(() => CaptureLoop(_captureLoopCts.Token));
        IsCapturing = true;

        return Task.FromResult(true);
    }

    public void StopCapture()
    {
        if (!IsCapturing)
            return;

        _captureLoopCts?.Cancel();
        _captureLoopTask?.Wait(1000);
        _captureLoopCts?.Dispose();
        _captureLoopCts = null;
        _captureLoopTask = null;
        IsCapturing = false;
    }

    public CapturedFrame? GetLatestFrame()
    {
        lock (_frameLock)
        {
            return _latestFrame;
        }
    }

    private async Task CaptureLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var frame = CaptureFrame();
                if (frame != null)
                {
                    lock (_frameLock)
                    {
                        _latestFrame?.Dispose();
                        _latestFrame = frame;
                    }

                    FrameCaptured?.Invoke(this, frame);
                }

                await Task.Delay(_captureIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture error: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private CapturedFrame? CaptureFrame()
    {
        Rectangle bounds;

        if (_captureWindow)
        {
            if (_windowHandle == IntPtr.Zero)
                return null;

            if (!User32.GetWindowRect(_windowHandle, out var rect))
                return null;

            bounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
        }
        else
        {
            bounds = GetMonitorBounds(_monitorIndex);
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        return CaptureRegion(bounds);
    }

    private static CapturedFrame? CaptureRegion(Rectangle bounds)
    {
        try
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            // Convert bitmap to byte array
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int stride = bitmapData.Stride;
                int size = stride * bitmap.Height;
                var data = new byte[size];

                Marshal.Copy(bitmapData.Scan0, data, 0, size);

                return new CapturedFrame
                {
                    Data = data,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    Stride = stride,
                    Timestamp = DateTime.UtcNow
                };
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error capturing region: {ex.Message}");
            return null;
        }
    }

    private static Rectangle GetMonitorBounds(int monitorIndex)
    {
        var monitors = GetMonitors();

        if (monitorIndex >= 0 && monitorIndex < monitors.Count)
            return monitors[monitorIndex].Bounds;

        // Fallback to primary monitor
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        return primary?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
    }

    /// <summary>
    /// Gets a list of available monitors.
    /// </summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int index = 0;

        bool EnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref User32.RECT lprcMonitor, IntPtr dwData)
        {
            var info = new User32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<User32.MONITORINFO>() };
            if (User32.GetMonitorInfoW(hMonitor, ref info))
            {
                var rect = info.rcMonitor;
                bool isPrimary = (info.dwFlags & 1) != 0;

                monitors.Add(new MonitorInfo
                {
                    Index = index,
                    Handle = hMonitor,
                    Bounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height),
                    IsPrimary = isPrimary,
                    DisplayName = isPrimary ? "Primary Monitor" : $"Monitor {index + 1}"
                });
            }
            index++;
            return true;
        }

        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, IntPtr.Zero);

        return monitors;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopCapture();

        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Information about a display monitor.
/// </summary>
public class MonitorInfo
{
    public int Index { get; set; }
    public IntPtr Handle { get; set; }
    public Rectangle Bounds { get; set; }
    public bool IsPrimary { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => $"{DisplayName} ({Bounds.Width}x{Bounds.Height})";
}
