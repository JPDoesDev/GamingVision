using System.Buffers;

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
    /// Captures a single frame immediately (synchronous).
    /// For WGC, this uses GDI fallback for single-shot capture.
    /// </summary>
    /// <returns>Captured frame or null if capture failed.</returns>
    CapturedFrame? CaptureFrame();

    /// <summary>
    /// Event raised when a new frame is captured.
    /// </summary>
    event EventHandler<CapturedFrame>? FrameCaptured;
}

/// <summary>
/// Represents a captured screen frame with pooled buffer support to reduce GC pressure.
/// </summary>
public class CapturedFrame : IDisposable
{
    private byte[]? _data;
    private bool _isPooled;

    /// <summary>
    /// Raw pixel data in BGRA format.
    /// </summary>
    public byte[] Data
    {
        get => _data ?? [];
        init
        {
            _data = value;
            DataLength = value?.Length ?? 0;
            _isPooled = false;
        }
    }

    /// <summary>
    /// Actual length of valid data in the buffer (may be less than Data.Length for pooled buffers).
    /// </summary>
    public int DataLength { get; private set; }

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
    /// Monotonically increasing frame counter for performance tracking.
    /// </summary>
    public ulong FrameId { get; init; }

    /// <summary>
    /// High-precision timestamp at capture start for performance measurement.
    /// Use with Stopwatch.GetTimestamp() to calculate elapsed time.
    /// </summary>
    public long CaptureStartTicks { get; init; }

    /// <summary>
    /// Whether this frame has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Creates a CapturedFrame with a pooled buffer from ArrayPool.
    /// The buffer will be returned to the pool on Dispose.
    /// </summary>
    public static CapturedFrame CreatePooled(int size, int width, int height, int stride,
        ulong frameId = 0, long captureStartTicks = 0)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        return new CapturedFrame(buffer, size, width, height, stride, frameId, captureStartTicks, isPooled: true);
    }

    /// <summary>
    /// Creates a CapturedFrame with an existing non-pooled buffer (legacy support).
    /// </summary>
    public static CapturedFrame CreateUnpooled(byte[] data, int width, int height, int stride,
        ulong frameId = 0, long captureStartTicks = 0)
    {
        return new CapturedFrame(data, data.Length, width, height, stride, frameId, captureStartTicks, isPooled: false);
    }

    private CapturedFrame(byte[] data, int dataLength, int width, int height, int stride,
        ulong frameId, long captureStartTicks, bool isPooled)
    {
        _data = data;
        DataLength = dataLength;
        Width = width;
        Height = height;
        Stride = stride;
        FrameId = frameId;
        CaptureStartTicks = captureStartTicks;
        _isPooled = isPooled;
    }

    /// <summary>
    /// Parameterless constructor for legacy compatibility.
    /// </summary>
    public CapturedFrame()
    {
        _data = [];
        _isPooled = false;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        // Return pooled buffer to ArrayPool
        if (_isPooled && _data != null)
        {
            ArrayPool<byte>.Shared.Return(_data);
            _data = null;
        }

        GC.SuppressFinalize(this);
    }
}
