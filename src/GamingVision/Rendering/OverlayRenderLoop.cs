using System.Diagnostics;
using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision.Rendering;

/// <summary>
/// Dedicated render loop that runs D2D overlay rendering on its own thread at a fixed framerate.
/// Completely decoupled from detection — always renders the latest available detections.
/// Detection results are passed in via lock-free Interlocked.Exchange.
/// </summary>
public class OverlayRenderLoop : IDisposable
{
    private Thread? _renderThread;
    private volatile bool _running;
    private bool _disposed;

    // Lock-free detection data: detection thread writes, render thread reads
    private volatile List<(DetectedObject detection, OverlayGroup group)>? _pendingItems;

    // Owned D2D resources (created and used exclusively on the render thread)
    private Direct2DOverlayWindow? _window;
    private Direct2DRenderer? _renderer;

    // Configuration set before starting
    private int _monitorIndex;
    private CrosshairSettings? _crosshairSettings;
    private bool _crosshairVisible;
    private readonly int _targetFps;
    private readonly double _frameIntervalMs;

    // Visibility control
    private volatile bool _overlayVisible = true;

    /// <summary>Whether the render loop is currently running.</summary>
    public bool IsRunning => _running;

    /// <summary>The native window handle (valid only while running).</summary>
    public IntPtr WindowHandle => _window?.Handle ?? IntPtr.Zero;

    public OverlayRenderLoop(int targetFps = 30)
    {
        _targetFps = targetFps;
        _frameIntervalMs = 1000.0 / targetFps;
    }

    /// <summary>
    /// Starts the render loop on a dedicated thread.
    /// Creates the D2D window and renderer on that thread.
    /// </summary>
    public void Start(int monitorIndex, CrosshairSettings? crosshairSettings, bool crosshairVisible)
    {
        if (_running) return;

        _monitorIndex = monitorIndex;
        _crosshairSettings = crosshairSettings;
        _crosshairVisible = crosshairVisible;
        _running = true;
        _pendingItems = null;

        _renderThread = new Thread(RenderThreadProc)
        {
            Name = "D2D-OverlayRender",
            IsBackground = true
        };
        _renderThread.SetApartmentState(ApartmentState.STA); // COM/D2D requires STA
        _renderThread.Start();
    }

    /// <summary>
    /// Stops the render loop and tears down D2D resources.
    /// Blocks until the render thread exits.
    /// </summary>
    public void Stop()
    {
        if (!_running) return;

        _running = false;
        _renderThread?.Join(2000); // Wait up to 2s for clean exit
        _renderThread = null;
        _pendingItems = null;
    }

    // Sentinel for "clear detections" vs "no new data"
    private static readonly List<(DetectedObject, OverlayGroup)> ClearSentinel = [];

    /// <summary>
    /// Publishes new detection results for the render loop to display.
    /// Lock-free: can be called from any thread (detection thread, UI thread, etc.).
    /// Pass null to clear all detections (e.g., when overlay is disabled).
    /// </summary>
    public void PublishDetections(List<(DetectedObject detection, OverlayGroup group)>? items)
    {
        // null means "clear", so use sentinel; empty list also means clear
        Interlocked.Exchange(ref _pendingItems, items ?? ClearSentinel);
    }

    /// <summary>
    /// Sets overlay visibility (toggle via hotkey).
    /// </summary>
    public void SetOverlayVisible(bool visible)
    {
        _overlayVisible = visible;
    }

    /// <summary>
    /// Updates crosshair settings. Thread-safe via volatile write.
    /// </summary>
    public void UpdateCrosshairSettings(CrosshairSettings? settings)
    {
        _crosshairSettings = settings;
        _crosshairVisible = settings != null;
    }

    /// <summary>
    /// Sets crosshair visibility independently.
    /// </summary>
    public void SetCrosshairVisible(bool visible)
    {
        _crosshairVisible = visible;
    }

    /// <summary>
    /// Shows or hides the window (used when both overlay and crosshair visibility change).
    /// Must be called carefully — the window is owned by the render thread.
    /// This sets a flag that the render thread will pick up.
    /// </summary>
    private volatile bool _windowVisibilityRequest = true;
    private volatile bool _windowVisibilityChanged;

    public void SetWindowVisible(bool visible)
    {
        _windowVisibilityRequest = visible;
        _windowVisibilityChanged = true;
    }

    private void RenderThreadProc()
    {
        try
        {
            // Create D2D resources on this thread (they must be used from the creating thread)
            _window = new Direct2DOverlayWindow();
            _window.Create();
            _window.PositionOverMonitor(_monitorIndex);

            _renderer = new Direct2DRenderer();
            _renderer.Initialize(_window.Handle, _window.PhysicalWidth, _window.PhysicalHeight);
            _renderer.DpiScale = _window.DpiScale;

            // Apply initial crosshair state
            if (_crosshairSettings != null)
            {
                _renderer.UpdateCrosshairSettings(_crosshairSettings);
                _renderer.SetCrosshairVisible(_crosshairVisible);
            }

            _window.Show();

            Logger.Log("RenderLoop", $"Started at {_targetFps}fps on dedicated thread");

            // Last rendered items (keep showing until new data arrives)
            List<(DetectedObject detection, OverlayGroup group)>? lastRendered = null;
            long lastRenderTicks = Stopwatch.GetTimestamp();

            while (_running)
            {
                var frameStart = Stopwatch.GetTimestamp();

                // Handle window visibility changes
                if (_windowVisibilityChanged)
                {
                    _windowVisibilityChanged = false;
                    if (_windowVisibilityRequest)
                        _window.Show();
                    else
                        _window.Hide();
                }

                // Apply crosshair settings changes
                _renderer.UpdateCrosshairSettings(_crosshairSettings);
                _renderer.SetCrosshairVisible(_crosshairVisible);

                // Atomically read latest detections (lock-free)
                var newItems = Interlocked.Exchange(ref _pendingItems, null!);
                if (newItems != null)
                {
                    // ClearSentinel (empty list) means explicitly clear detections
                    lastRendered = newItems.Count > 0 ? newItems : null;
                }

                // Always render at target FPS — shows latest available detections
                if (_overlayVisible)
                {
                    _renderer.Render(lastRendered);
                }
                else
                {
                    // Overlay hidden but crosshair might still be visible
                    _renderer.Render(null);
                }

                // Precise frame timing using spin-wait for sub-ms accuracy
                var elapsed = (double)(Stopwatch.GetTimestamp() - frameStart) / Stopwatch.Frequency * 1000.0;
                var sleepMs = _frameIntervalMs - elapsed;

                if (sleepMs > 2.0)
                {
                    // Sleep for most of the wait, then spin for precision
                    Thread.Sleep((int)(sleepMs - 1.5));
                }

                // Spin-wait for remaining time (sub-ms precision)
                while ((double)(Stopwatch.GetTimestamp() - frameStart) / Stopwatch.Frequency * 1000.0 < _frameIntervalMs)
                {
                    Thread.SpinWait(10);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("RenderLoop: Fatal error on render thread", ex);
        }
        finally
        {
            // Tear down D2D resources on the same thread that created them
            _renderer?.Dispose();
            _renderer = null;
            _window?.Dispose();
            _window = null;

            Logger.Log("RenderLoop", "Stopped");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        GC.SuppressFinalize(this);
    }
}
