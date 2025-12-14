namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Interface for screen capture services.
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    /// <summary>
    /// Gets whether the capture service is currently running.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Starts capturing frames.
    /// </summary>
    /// <returns>True if capture started successfully.</returns>
    Task<bool> StartCaptureAsync();

    /// <summary>
    /// Stops capturing frames.
    /// </summary>
    void StopCapture();

    /// <summary>
    /// Gets the most recent captured frame as a byte array (BGRA format).
    /// </summary>
    /// <returns>Frame data or null if no frame available.</returns>
    CapturedFrame? GetLatestFrame();

    /// <summary>
    /// Event raised when a new frame is captured.
    /// </summary>
    event EventHandler<CapturedFrame>? FrameCaptured;
}

/// <summary>
/// Represents a captured screen frame.
/// </summary>
public class CapturedFrame : IDisposable
{
    /// <summary>
    /// Raw pixel data in BGRA format.
    /// </summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    /// Width of the frame in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of the frame in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Stride (bytes per row) of the frame.
    /// </summary>
    public int Stride { get; init; }

    /// <summary>
    /// Timestamp when the frame was captured.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this frame has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
}
