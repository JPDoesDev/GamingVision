using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Screen capture service using DXGI Desktop Duplication API.
/// Captures at the GPU adapter output level, supporting exclusive fullscreen games
/// that bypass the DWM compositor (where WGC fails).
/// </summary>
public class DxgiCaptureService : IScreenCaptureService
{
    private IntPtr _device;
    private IntPtr _context;
    private IntPtr _duplication;
    private IntPtr _stagingTexture;

    // Cached COM wrappers to avoid repeated GetObjectForIUnknown calls
    private Dxgi.IDXGIOutputDuplication? _duplicationWrapper;
    private D3D11Interop.ID3D11DeviceContext? _contextWrapper;

    private int _screenWidth;
    private int _screenHeight;

    // Capture mode
    private IntPtr _targetWindow;
    private int _monitorIndex;
    private bool _captureWindow;
    private int _captureIntervalMs = 33; // ~30 FPS

    // Capture state
    private CancellationTokenSource? _cts;
    private Task? _captureLoopTask;
    private CapturedFrame? _latestFrame;
    private readonly object _frameLock = new();
    private bool _disposed;

    // Frame tracking
    private static ulong _frameCounter;
    private readonly SemaphoreSlim _frameProcessingSemaphore = new(2, 2);

    // GDI fallback for synchronous single-shot capture (training data)
    private readonly GdiCaptureService _fallbackService = new();

    // DXGI recovery
    private int _reinitAttempts;
    private const int MaxReinitAttempts = 3;

    public bool IsCapturing { get; private set; }

    public event EventHandler<CapturedFrame>? FrameCaptured;

    /// <summary>
    /// Initializes capture for a specific window by handle.
    /// DXGI DD captures the full monitor and crops to the window rect each frame.
    /// </summary>
    public void InitializeForWindow(IntPtr windowHandle)
    {
        _targetWindow = windowHandle;
        _captureWindow = true;

        // Determine which monitor the window is on
        var hMonitor = User32.MonitorFromWindow(windowHandle, User32.MONITOR_DEFAULTTONEAREST);
        _monitorIndex = GetMonitorIndex(hMonitor);

        // Initialize GDI fallback for CaptureFrame() (training data)
        _fallbackService.InitializeForWindow(windowHandle);
    }

    /// <summary>
    /// Initializes capture for a specific window by title.
    /// </summary>
    public bool InitializeForWindowTitle(string windowTitle)
    {
        var handle = WindowFinder.FindWindowByTitle(windowTitle);
        if (handle == IntPtr.Zero)
            return false;

        InitializeForWindow(handle);
        return true;
    }

    /// <summary>
    /// Initializes capture for a specific monitor (fullscreen mode).
    /// </summary>
    public void InitializeForMonitor(int monitorIndex)
    {
        _monitorIndex = monitorIndex;
        _captureWindow = false;

        // Initialize GDI fallback for CaptureFrame() (training data)
        _fallbackService.InitializeForMonitor(monitorIndex);
    }

    /// <summary>
    /// Sets the capture interval in milliseconds.
    /// </summary>
    public void SetCaptureInterval(int intervalMs)
    {
        _captureIntervalMs = Math.Max(16, intervalMs);
        _fallbackService.SetCaptureInterval(intervalMs);
    }

    public async Task<bool> StartCaptureAsync()
    {
        if (IsCapturing)
            return true;

        try
        {
            Logger.Log("DxgiCaptureService: Initializing D3D11...");
            if (!InitializeD3D11())
            {
                Logger.Warn("DxgiCaptureService: D3D11 init failed");
                return false;
            }

            Logger.Log($"DxgiCaptureService: Initializing duplication for monitor {_monitorIndex}...");
            if (!InitializeDuplication(_monitorIndex))
            {
                Logger.Warn("DxgiCaptureService: Duplication init failed");
                CleanupDxgiResources();
                return false;
            }

            Logger.Log($"DxgiCaptureService: Capture started ({_screenWidth}x{_screenHeight})");

            _cts = new CancellationTokenSource();
            _captureLoopTask = Task.Run(() => CaptureLoop(_cts.Token));
            IsCapturing = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("DxgiCaptureService: StartCaptureAsync failed", ex);
            CleanupDxgiResources();
            return false;
        }
    }

    public void StopCapture()
    {
        if (!IsCapturing)
            return;

        Logger.Log("DxgiCaptureService: Stopping capture");
        _cts?.Cancel();
        _captureLoopTask?.Wait(2000);
        CleanupDxgiResources();
        IsCapturing = false;
        Logger.Log("DxgiCaptureService: Capture stopped");
    }

    public CapturedFrame? GetLatestFrame()
    {
        lock (_frameLock)
        {
            return _latestFrame;
        }
    }

    /// <summary>
    /// Captures a single frame synchronously using GDI PrintWindow fallback.
    /// Used for training data capture (bypasses overlays).
    /// </summary>
    public CapturedFrame? CaptureFrame()
    {
        return _fallbackService.CaptureFrame();
    }

    #region D3D11 / DXGI Initialization

    private bool InitializeD3D11()
    {
        var featureLevels = new[]
        {
            D3D11Interop.D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D11Interop.D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
            D3D11Interop.D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
            D3D11Interop.D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
        };

        var hr = D3D11Interop.D3D11CreateDevice(
            IntPtr.Zero,
            D3D11Interop.D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11Interop.D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels,
            (uint)featureLevels.Length,
            D3D11Interop.D3D11_SDK_VERSION,
            out _device,
            out var actualFeatureLevel,
            out _context);

        Logger.Log($"DxgiCaptureService: D3D11CreateDevice HR=0x{hr:X8}, FeatureLevel={actualFeatureLevel}");
        return hr >= 0 && _device != IntPtr.Zero;
    }

    private bool InitializeDuplication(int monitorIndex)
    {
        // Get DXGI device from D3D11 device
        var dxgiDeviceGuid = Dxgi.IID_IDXGIDevice;
        Marshal.QueryInterface(_device, ref dxgiDeviceGuid, out var dxgiDevicePtr);

        if (dxgiDevicePtr == IntPtr.Zero)
        {
            Logger.Warn("DxgiCaptureService: Failed to get IDXGIDevice");
            return false;
        }

        try
        {
            var dxgiDevice = (Dxgi.IDXGIDevice)Marshal.GetObjectForIUnknown(dxgiDevicePtr);
            dxgiDevice.GetAdapter(out var adapterPtr);

            if (adapterPtr == IntPtr.Zero)
            {
                Logger.Warn("DxgiCaptureService: Failed to get adapter");
                return false;
            }

            try
            {
                var adapter = (Dxgi.IDXGIAdapter)Marshal.GetObjectForIUnknown(adapterPtr);
                var hr = adapter.EnumOutputs((uint)monitorIndex, out var outputPtr);

                if (hr < 0 || outputPtr == IntPtr.Zero)
                {
                    Logger.Warn($"DxgiCaptureService: EnumOutputs failed HR=0x{hr:X8}");
                    return false;
                }

                try
                {
                    var output1Guid = Dxgi.IID_IDXGIOutput1;
                    Marshal.QueryInterface(outputPtr, ref output1Guid, out var output1Ptr);

                    if (output1Ptr == IntPtr.Zero)
                    {
                        Logger.Warn("DxgiCaptureService: Failed to get IDXGIOutput1");
                        return false;
                    }

                    try
                    {
                        var output1 = (Dxgi.IDXGIOutput1)Marshal.GetObjectForIUnknown(output1Ptr);

                        output1.GetDesc(out var desc);
                        _screenWidth = desc.DesktopCoordinates.Width;
                        _screenHeight = desc.DesktopCoordinates.Height;
                        Logger.Log($"DxgiCaptureService: Monitor {desc.DeviceName} {_screenWidth}x{_screenHeight}");

                        hr = output1.DuplicateOutput(_device, out _duplication);

                        if (hr < 0 || _duplication == IntPtr.Zero)
                        {
                            if (hr == Dxgi.DXGI_ERROR_NOT_CURRENTLY_AVAILABLE)
                                Logger.Warn("DxgiCaptureService: DXGI_ERROR_NOT_CURRENTLY_AVAILABLE - another app may be using desktop duplication");
                            else
                                Logger.Warn($"DxgiCaptureService: DuplicateOutput failed HR=0x{hr:X8}");
                            return false;
                        }

                        CreateStagingTexture();
                        _reinitAttempts = 0;
                        return true;
                    }
                    finally
                    {
                        Marshal.Release(output1Ptr);
                    }
                }
                finally
                {
                    Marshal.Release(outputPtr);
                }
            }
            finally
            {
                Marshal.Release(adapterPtr);
            }
        }
        finally
        {
            Marshal.Release(dxgiDevicePtr);
        }
    }

    private void CreateStagingTexture()
    {
        var desc = new D3D11Interop.D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_screenWidth,
            Height = (uint)_screenHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = D3D11Interop.DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new D3D11Interop.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11Interop.D3D11_USAGE.D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = (uint)D3D11Interop.D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0
        };

        var device = (D3D11Interop.ID3D11Device)Marshal.GetObjectForIUnknown(_device);
        var hr = device.CreateTexture2D(ref desc, IntPtr.Zero, out _stagingTexture);

        if (hr < 0)
            Logger.Warn($"DxgiCaptureService: CreateTexture2D failed HR=0x{hr:X8}");
    }

    #endregion

    #region Capture Loop

    private void CaptureLoop(CancellationToken token)
    {
        Logger.Log($"DxgiCaptureService: Capture loop started ({1000 / _captureIntervalMs} FPS target)");

        // Create COM wrappers on this thread to avoid cross-thread COM issues
        try
        {
            _duplicationWrapper = (Dxgi.IDXGIOutputDuplication)Marshal.GetObjectForIUnknown(_duplication);
            _contextWrapper = (D3D11Interop.ID3D11DeviceContext)Marshal.GetObjectForIUnknown(_context);
        }
        catch (Exception ex)
        {
            Logger.Error("DxgiCaptureService: Failed to create COM wrappers", ex);
            return;
        }

        var stopwatch = new Stopwatch();

        while (!token.IsCancellationRequested)
        {
            stopwatch.Restart();

            try
            {
                CaptureOneFrame();
            }
            catch (Exception ex)
            {
                Logger.Error("DxgiCaptureService: Capture loop error", ex);
                Thread.Sleep(100);
            }

            // Precise frame timing
            stopwatch.Stop();
            var sleepTime = _captureIntervalMs - (int)stopwatch.ElapsedMilliseconds;
            if (sleepTime > 0)
                Thread.Sleep(sleepTime);
        }

        Logger.Log("DxgiCaptureService: Capture loop ended");
    }

    private void CaptureOneFrame()
    {
        if (_duplicationWrapper == null || _contextWrapper == null)
            return;

        var frameId = Interlocked.Increment(ref _frameCounter);
        var captureStartTicks = Stopwatch.GetTimestamp();

        var hr = _duplicationWrapper.AcquireNextFrame(100, out var frameInfo, out var desktopResource);

        if (hr == Dxgi.DXGI_ERROR_WAIT_TIMEOUT)
            return; // No new frame

        if (hr == Dxgi.DXGI_ERROR_ACCESS_LOST)
        {
            Logger.Warn("DxgiCaptureService: DXGI_ERROR_ACCESS_LOST - attempting recovery");
            TryRecoverFromAccessLost();
            return;
        }

        if (hr < 0)
        {
            Logger.Warn($"DxgiCaptureService: AcquireNextFrame failed HR=0x{hr:X8}");
            return;
        }

        if (desktopResource == IntPtr.Zero)
        {
            _duplicationWrapper.ReleaseFrame();
            return;
        }

        try
        {
            // Get the texture from the desktop resource
            var textureGuid = D3D11Interop.IID_ID3D11Texture2D;
            Marshal.QueryInterface(desktopResource, ref textureGuid, out var texturePtr);

            if (texturePtr == IntPtr.Zero)
                return;

            try
            {
                // GPU → GPU copy to staging texture
                _contextWrapper.CopyResource(_stagingTexture, texturePtr);

                // Map staging texture for CPU read
                hr = _contextWrapper.Map(
                    _stagingTexture, 0,
                    D3D11Interop.D3D11_MAP.D3D11_MAP_READ, 0,
                    out var mappedResource);

                if (hr < 0)
                    return;

                try
                {
                    if (mappedResource.pData == IntPtr.Zero)
                        return;

                    CapturedFrame? frame;

                    if (_captureWindow && _targetWindow != IntPtr.Zero)
                    {
                        frame = ExtractWindowRegion(
                            mappedResource.pData, (int)mappedResource.RowPitch,
                            frameId, captureStartTicks);
                    }
                    else
                    {
                        frame = ExtractFullScreen(
                            mappedResource.pData, (int)mappedResource.RowPitch,
                            frameId, captureStartTicks);
                    }

                    if (frame != null)
                        DeliverFrame(frame);
                }
                finally
                {
                    _contextWrapper.Unmap(_stagingTexture, 0);
                }
            }
            finally
            {
                Marshal.Release(texturePtr);
            }
        }
        finally
        {
            Marshal.Release(desktopResource);
            _duplicationWrapper.ReleaseFrame();
        }
    }

    /// <summary>
    /// Extracts the full screen as a CapturedFrame.
    /// </summary>
    private CapturedFrame? ExtractFullScreen(IntPtr sourceData, int sourceRowPitch,
        ulong frameId, long captureStartTicks)
    {
        int stride = _screenWidth * 4;
        int size = stride * _screenHeight;

        var frame = CapturedFrame.CreatePooled(size, _screenWidth, _screenHeight, stride,
            frameId, captureStartTicks);

        CopyTextureData(sourceData, sourceRowPitch, frame.Data, stride, _screenWidth, _screenHeight);
        return frame;
    }

    /// <summary>
    /// Extracts a window region from the full screen capture.
    /// Crops to the window's current rect each frame.
    /// </summary>
    private CapturedFrame? ExtractWindowRegion(IntPtr sourceData, int sourceRowPitch,
        ulong frameId, long captureStartTicks)
    {
        if (!User32.GetWindowRect(_targetWindow, out var windowRect))
            return null;

        // Clamp to screen bounds
        int srcX = Math.Max(0, Math.Min(windowRect.Left, _screenWidth - 1));
        int srcY = Math.Max(0, Math.Min(windowRect.Top, _screenHeight - 1));
        int regionWidth = Math.Min(windowRect.Width, _screenWidth - srcX);
        int regionHeight = Math.Min(windowRect.Height, _screenHeight - srcY);

        if (regionWidth <= 0 || regionHeight <= 0)
            return null;

        int stride = regionWidth * 4;
        int size = stride * regionHeight;

        var frame = CapturedFrame.CreatePooled(size, regionWidth, regionHeight, stride,
            frameId, captureStartTicks);

        // Copy only the window region row-by-row
        unsafe
        {
            byte* srcBase = (byte*)sourceData;
            fixed (byte* dstBase = frame.Data)
            {
                for (int y = 0; y < regionHeight; y++)
                {
                    byte* srcRow = srcBase + (srcY + y) * sourceRowPitch + srcX * 4;
                    byte* dstRow = dstBase + y * stride;
                    Buffer.MemoryCopy(srcRow, dstRow, stride, stride);
                }
            }
        }

        return frame;
    }

    /// <summary>
    /// Copies texture data handling row pitch differences.
    /// </summary>
    private static void CopyTextureData(IntPtr source, int sourceRowPitch,
        byte[] dest, int destStride, int width, int height)
    {
        if (sourceRowPitch == destStride)
        {
            // Single block copy when pitches match
            Marshal.Copy(source, dest, 0, destStride * height);
        }
        else
        {
            // Row-by-row copy when GPU added padding
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(source + row * sourceRowPitch, dest, row * destStride, destStride);
            }
        }
    }

    /// <summary>
    /// Delivers a captured frame to subscribers via event and polling.
    /// </summary>
    private void DeliverFrame(CapturedFrame frame)
    {
        // Update latest frame for polling access
        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = frame;
        }

        // Fire event handler (skip if backed up)
        var handler = FrameCaptured;
        if (handler != null)
        {
            if (!_frameProcessingSemaphore.Wait(0))
            {
                // Processing backed up - skip frame
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    handler.Invoke(this, frame);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Frame handler error: {ex.Message}");
                }
                finally
                {
                    _frameProcessingSemaphore.Release();
                }
            });
        }
    }

    #endregion

    #region DXGI Recovery

    /// <summary>
    /// Attempts to recover from DXGI_ERROR_ACCESS_LOST (mode switch, resolution change).
    /// </summary>
    private void TryRecoverFromAccessLost()
    {
        _reinitAttempts++;
        if (_reinitAttempts > MaxReinitAttempts)
        {
            Logger.Warn($"DxgiCaptureService: Recovery failed after {MaxReinitAttempts} attempts");
            return;
        }

        Logger.Log($"DxgiCaptureService: Recovery attempt {_reinitAttempts}/{MaxReinitAttempts}");

        // Release duplication resources (keep D3D11 device)
        ReleaseDuplicationResources();

        // Brief delay for mode switch to complete
        Thread.Sleep(500);

        // Reinitialize duplication
        if (InitializeDuplication(_monitorIndex))
        {
            // Recreate COM wrappers on this thread
            try
            {
                _duplicationWrapper = (Dxgi.IDXGIOutputDuplication)Marshal.GetObjectForIUnknown(_duplication);
                Logger.Log("DxgiCaptureService: Recovery successful");
            }
            catch (Exception ex)
            {
                Logger.Warn($"DxgiCaptureService: Recovery COM wrapper failed: {ex.Message}");
            }
        }
        else
        {
            Logger.Warn("DxgiCaptureService: Recovery InitializeDuplication failed");
        }
    }

    private void ReleaseDuplicationResources()
    {
        if (_duplicationWrapper != null)
        {
            try { Marshal.ReleaseComObject(_duplicationWrapper); } catch { }
            _duplicationWrapper = null;
        }

        if (_stagingTexture != IntPtr.Zero)
        {
            Marshal.Release(_stagingTexture);
            _stagingTexture = IntPtr.Zero;
        }

        if (_duplication != IntPtr.Zero)
        {
            Marshal.Release(_duplication);
            _duplication = IntPtr.Zero;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Gets the monitor index for a given HMONITOR handle.
    /// </summary>
    private static int GetMonitorIndex(IntPtr hMonitor)
    {
        var monitors = GdiCaptureService.GetMonitors();
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Handle == hMonitor)
                return i;
        }
        return 0; // Default to primary
    }

    #endregion

    #region Cleanup

    private void CleanupDxgiResources()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _captureLoopTask = null;

        ReleaseDuplicationResources();

        if (_contextWrapper != null)
        {
            try { Marshal.ReleaseComObject(_contextWrapper); } catch { }
            _contextWrapper = null;
        }

        if (_context != IntPtr.Zero)
        {
            Marshal.Release(_context);
            _context = IntPtr.Zero;
        }

        if (_device != IntPtr.Zero)
        {
            Marshal.Release(_device);
            _device = IntPtr.Zero;
        }
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

        _fallbackService.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
