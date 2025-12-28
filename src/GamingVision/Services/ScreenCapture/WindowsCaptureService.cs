using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using GamingVision.Native;
using GamingVision.Utilities;
using WinRT;

namespace GamingVision.Services.ScreenCapture;

/// <summary>
/// Screen capture service using Windows.Graphics.Capture API.
/// Provides GPU-accelerated capture with ~3-10ms latency vs ~70-90ms for GDI.
/// Falls back to GDI capture if WGC is not available.
/// </summary>
public class WindowsCaptureService : IScreenCaptureService
{
    private readonly GdiCaptureService _fallbackService;
    private bool _useWgc;
    private bool _disposed;

    // WGC resources
    private IntPtr _d3dDevice;
    private IntPtr _d3dContext;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private SizeInt32 _lastSize;
    private IntPtr _stagingTexture;

    // Capture state
    private IntPtr _targetWindow;
    private IntPtr _targetMonitor;
    private bool _captureWindow;
    private int _monitorIndex;
    private CapturedFrame? _latestFrame;
    private readonly object _frameLock = new();
    private int _captureIntervalMs = 33; // ~30 FPS

    // Frame ID counter for performance tracking
    private static ulong _frameCounter;

    public bool IsCapturing { get; private set; }

    public event EventHandler<CapturedFrame>? FrameCaptured;

    public WindowsCaptureService()
    {
        _fallbackService = new GdiCaptureService();
        _useWgc = IsWgcSupported();

        if (_useWgc)
        {
            try
            {
                InitializeD3D();
                Logger.Log("WindowsCaptureService: WGC initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Warn($"WindowsCaptureService: WGC init failed, using GDI fallback: {ex.Message}");
                _useWgc = false;
            }
        }
        else
        {
            Logger.Log("WindowsCaptureService: WGC not supported, using GDI fallback");
        }
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
    /// Initializes Direct3D11 device for WGC.
    /// </summary>
    private void InitializeD3D()
    {
        // Create D3D11 device with BGRA support for WGC
        var featureLevels = new D3D_FEATURE_LEVEL[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
        };

        var hr = D3D11.D3D11CreateDevice(
            IntPtr.Zero, // Default adapter
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels,
            (uint)featureLevels.Length,
            D3D11.D3D11_SDK_VERSION,
            out _d3dDevice,
            out _,
            out _d3dContext);

        if (hr != 0 || _d3dDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create D3D11 device: 0x{hr:X8}");
        }

        // Get DXGI device for WinRT interop
        var dxgiGuid = typeof(IDXGIDevice).GUID;
        hr = Marshal.QueryInterface(_d3dDevice, ref dxgiGuid, out var dxgiDevice);
        if (hr != 0)
        {
            throw new InvalidOperationException($"Failed to get DXGI device: 0x{hr:X8}");
        }

        try
        {
            _winrtDevice = CreateDirect3DDeviceFromDXGIDevice(dxgiDevice);
        }
        finally
        {
            Marshal.Release(dxgiDevice);
        }
    }

    /// <summary>
    /// Creates a WinRT Direct3D device from a DXGI device.
    /// </summary>
    private static IDirect3DDevice CreateDirect3DDeviceFromDXGIDevice(IntPtr dxgiDevice)
    {
        var hr = D3D11.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{hr:X8}");
        }

        return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
    }

    /// <summary>
    /// Initializes capture for a specific window by title.
    /// </summary>
    public bool InitializeForWindowTitle(string windowTitle)
    {
        _targetWindow = WindowFinder.FindWindowByTitle(windowTitle);
        if (_targetWindow == IntPtr.Zero)
        {
            Debug.WriteLine($"Window not found: {windowTitle}");
            return false;
        }

        _captureWindow = true;

        // Always initialize fallback service for CaptureFrame() support
        _fallbackService.InitializeForWindowTitle(windowTitle);

        return true;
    }

    /// <summary>
    /// Initializes capture for a specific window by handle.
    /// </summary>
    public void InitializeForWindow(IntPtr windowHandle)
    {
        _targetWindow = windowHandle;
        _captureWindow = true;

        // Always initialize fallback service for CaptureFrame() support
        _fallbackService.InitializeForWindow(windowHandle);
    }

    /// <summary>
    /// Initializes capture for a specific monitor.
    /// </summary>
    public void InitializeForMonitor(int monitorIndex)
    {
        _monitorIndex = monitorIndex;
        _captureWindow = false;

        // Get HMONITOR for the specified index
        _targetMonitor = GetMonitorHandle(monitorIndex);

        // Always initialize fallback service for CaptureFrame() support
        _fallbackService.InitializeForMonitor(monitorIndex);
    }

    /// <summary>
    /// Gets the HMONITOR for a given monitor index.
    /// </summary>
    private static IntPtr GetMonitorHandle(int index)
    {
        var monitors = new List<IntPtr>();

        bool EnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref User32.RECT lprcMonitor, IntPtr dwData)
        {
            monitors.Add(hMonitor);
            return true;
        }

        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, IntPtr.Zero);

        if (index >= 0 && index < monitors.Count)
        {
            return monitors[index];
        }

        return monitors.FirstOrDefault();
    }

    /// <summary>
    /// Sets the capture interval in milliseconds.
    /// </summary>
    public void SetCaptureInterval(int intervalMs)
    {
        _captureIntervalMs = Math.Max(16, intervalMs); // Minimum 16ms (~60 FPS max)
        _fallbackService.SetCaptureInterval(intervalMs);
    }

    public async Task<bool> StartCaptureAsync()
    {
        if (IsCapturing)
            return true;

        if (!_useWgc)
        {
            _fallbackService.FrameCaptured += OnFallbackFrameCaptured;
            IsCapturing = await _fallbackService.StartCaptureAsync();
            return IsCapturing;
        }

        try
        {
            // Create capture item
            _captureItem = _captureWindow
                ? CreateCaptureItemForWindow(_targetWindow)
                : CreateCaptureItemForMonitor(_targetMonitor);

            if (_captureItem == null)
            {
                Logger.Warn("WindowsCaptureService: Failed to create capture item, falling back to GDI");
                _useWgc = false;
                _fallbackService.FrameCaptured += OnFallbackFrameCaptured;
                IsCapturing = await _fallbackService.StartCaptureAsync();
                return IsCapturing;
            }

            _lastSize = _captureItem.Size;

            // Create frame pool
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2, // Number of buffers
                _lastSize);

            _framePool.FrameArrived += OnFrameArrived;

            // Create and start capture session
            _captureSession = _framePool.CreateCaptureSession(_captureItem);
            _captureSession.IsBorderRequired = false;
            _captureSession.IsCursorCaptureEnabled = false;
            _captureSession.StartCapture();

            IsCapturing = true;
            Logger.Log($"WindowsCaptureService: WGC capture started ({_lastSize.Width}x{_lastSize.Height})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"WindowsCaptureService: WGC start failed: {ex.Message}, falling back to GDI");
            _useWgc = false;
            CleanupWgcResources();
            _fallbackService.FrameCaptured += OnFallbackFrameCaptured;
            IsCapturing = await _fallbackService.StartCaptureAsync();
            return IsCapturing;
        }
    }

    /// <summary>
    /// Creates a GraphicsCaptureItem for a window handle.
    /// </summary>
    private static GraphicsCaptureItem? CreateCaptureItemForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            Logger.Warn("CreateCaptureItemForWindow: Window handle is null/zero");
            return null;
        }

        var factory = GraphicsCaptureItemInterop.Factory;
        if (factory == null)
        {
            Logger.Warn($"CreateCaptureItemForWindow: WGC factory not available - {GraphicsCaptureItemInterop.InitializationError}");
            return null;
        }

        try
        {
            Logger.Log($"CreateCaptureItemForWindow: Attempting WGC capture for hwnd=0x{hwnd:X}");

            // Get the IID for GraphicsCaptureItem (IGraphicsCaptureItem interface)
            // This is the WinRT interface GUID, not the runtime class GUID
            var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

            var hr = factory.CreateForWindow(hwnd, ref iid, out var ptr);
            if (hr != 0)
            {
                Logger.Warn($"CreateCaptureItemForWindow: CreateForWindow failed with HRESULT 0x{hr:X8} for hwnd=0x{hwnd:X}");
                return null;
            }

            if (ptr == IntPtr.Zero)
            {
                Logger.Warn($"CreateCaptureItemForWindow: CreateForWindow returned null for hwnd=0x{hwnd:X}");
                return null;
            }

            // Marshal the COM pointer to a WinRT GraphicsCaptureItem using CsWinRT interop
            var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);

            Logger.Log($"CreateCaptureItemForWindow: Success, size={item.Size.Width}x{item.Size.Height}");
            return item;
        }
        catch (Exception ex)
        {
            // Common reasons for failure:
            // - Window uses hardware overlay (some games)
            // - Window has anti-capture protection
            // - Window is minimized or invisible
            // - Access denied (elevated process)
            Logger.Warn($"CreateCaptureItemForWindow: Failed for hwnd=0x{hwnd:X} - {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a GraphicsCaptureItem for a monitor handle.
    /// </summary>
    private static GraphicsCaptureItem? CreateCaptureItemForMonitor(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
        {
            Logger.Warn("CreateCaptureItemForMonitor: Monitor handle is null/zero");
            return null;
        }

        var factory = GraphicsCaptureItemInterop.Factory;
        if (factory == null)
        {
            Logger.Warn($"CreateCaptureItemForMonitor: WGC factory not available - {GraphicsCaptureItemInterop.InitializationError}");
            return null;
        }

        try
        {
            Logger.Log($"CreateCaptureItemForMonitor: Attempting WGC capture for hMonitor=0x{hMonitor:X}");

            // Get the IID for GraphicsCaptureItem (IGraphicsCaptureItem interface)
            // This is the WinRT interface GUID, not the runtime class GUID
            var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

            var hr = factory.CreateForMonitor(hMonitor, ref iid, out var ptr);
            if (hr != 0)
            {
                Logger.Warn($"CreateCaptureItemForMonitor: CreateForMonitor failed with HRESULT 0x{hr:X8} for hMonitor=0x{hMonitor:X}");
                return null;
            }

            if (ptr == IntPtr.Zero)
            {
                Logger.Warn($"CreateCaptureItemForMonitor: CreateForMonitor returned null for hMonitor=0x{hMonitor:X}");
                return null;
            }

            // Marshal the COM pointer to a WinRT GraphicsCaptureItem using CsWinRT interop
            var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);

            Logger.Log($"CreateCaptureItemForMonitor: Success, size={item.Size.Width}x{item.Size.Height}");
            return item;
        }
        catch (Exception ex)
        {
            Logger.Warn($"CreateCaptureItemForMonitor: Failed for hMonitor=0x{hMonitor:X} - {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Handles new frames from WGC.
    /// </summary>
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        // Generate frame ID and capture start time for performance tracking
        var frameId = Interlocked.Increment(ref _frameCounter);
        var captureStartTicks = Stopwatch.GetTimestamp();

        Logger.PerfFrame(frameId, "CAPTURE", "WGC frame arrived");

        using var frame = sender.TryGetNextFrame();
        if (frame == null)
            return;

        Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", "Frame retrieved from pool");

        // Check if size changed
        var newSize = frame.ContentSize;
        if (newSize.Width != _lastSize.Width || newSize.Height != _lastSize.Height)
        {
            _lastSize = newSize;
            if (_stagingTexture != IntPtr.Zero)
            {
                Marshal.Release(_stagingTexture);
                _stagingTexture = IntPtr.Zero;
            }

            // Recreate frame pool with new size
            _framePool?.Recreate(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _lastSize);
        }

        // Get the D3D texture from the frame
        var surfaceTexture = GetDXGIInterfaceFromObject(frame.Surface, frameId, captureStartTicks);
        if (surfaceTexture == IntPtr.Zero)
        {
            Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", "ERROR: Failed to get D3D texture from surface");
            return;
        }

        try
        {
            // Ensure staging texture exists
            EnsureStagingTexture(newSize.Width, newSize.Height);

            Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", "Staging texture ready");

            // Copy to staging texture for CPU read
            D3D11.CopyResource(_d3dContext, _stagingTexture, surfaceTexture);

            Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", "GPU copy complete");

            // Map and read the data
            var capturedFrame = ReadStagingTexture(newSize.Width, newSize.Height, frameId, captureStartTicks);

            Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", $"CPU readback complete ({newSize.Width}x{newSize.Height})");

            if (capturedFrame != null)
            {
                // Fire event first - handler takes ownership and is responsible for the frame
                // Don't dispose frames that are passed to event handlers
                var handler = FrameCaptured;
                if (handler != null)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            handler.Invoke(this, capturedFrame);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Frame handler error: {ex.Message}");
                        }
                    });
                }

                // Update latest frame for polling access (separate from event-based access)
                // Only dispose if no event handlers - otherwise event handler owns the frame
                if (handler == null)
                {
                    lock (_frameLock)
                    {
                        _latestFrame?.Dispose();
                        _latestFrame = capturedFrame;
                    }
                }
            }
        }
        finally
        {
            Marshal.Release(surfaceTexture);
        }
    }

    /// <summary>
    /// Gets a D3D interface from a WinRT IDirect3DSurface using native ABI interop.
    /// </summary>
    private static IntPtr GetDXGIInterfaceFromObject(object obj, ulong frameId, long captureStartTicks)
    {
        if (obj == null)
        {
            Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", "ERROR: Surface object is null");
            return IntPtr.Zero;
        }

        try
        {
            // Cast to IDirect3DSurface first to ensure we have the right type
            if (obj is not IDirect3DSurface surface)
            {
                Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", $"ERROR: Object is not IDirect3DSurface (type: {obj.GetType().Name})");
                return IntPtr.Zero;
            }

            // Get the native ABI pointer using CsWinRT interop
            var abiPtr = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            if (abiPtr == IntPtr.Zero)
            {
                Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", "ERROR: Failed to get ABI pointer from surface");
                return IntPtr.Zero;
            }

            try
            {
                // QueryInterface for IDirect3DDxgiInterfaceAccess on the native pointer
                var accessIid = typeof(IDirect3DDxgiInterfaceAccess).GUID;
                var hr = Marshal.QueryInterface(abiPtr, ref accessIid, out var accessPtr);
                if (hr != 0 || accessPtr == IntPtr.Zero)
                {
                    Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", $"ERROR: QueryInterface for IDirect3DDxgiInterfaceAccess failed (hr=0x{hr:X8})");
                    return IntPtr.Zero;
                }

                try
                {
                    // Use vtable call to get the D3D11 texture interface
                    var textureIid = typeof(ID3D11Texture2D).GUID;
                    var texturePtr = IntPtr.Zero;

                    // IDirect3DDxgiInterfaceAccess::GetInterface is at vtable slot 3 (after IUnknown methods)
                    // HRESULT GetInterface(REFIID iid, void** p)
                    var vtable = Marshal.ReadIntPtr(accessPtr);
                    var getInterfacePtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                    var getInterface = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(getInterfacePtr);

                    hr = getInterface(accessPtr, ref textureIid, out texturePtr);
                    if (hr != 0 || texturePtr == IntPtr.Zero)
                    {
                        Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", $"ERROR: GetInterface failed (hr=0x{hr:X8})");
                        return IntPtr.Zero;
                    }

                    return texturePtr;
                }
                finally
                {
                    Marshal.Release(accessPtr);
                }
            }
            finally
            {
                Marshal.Release(abiPtr);
            }
        }
        catch (Exception ex)
        {
            Logger.PerfFrameTimed(frameId, captureStartTicks, "CAPTURE", $"ERROR: GetDXGIInterface threw {ex.GetType().Name}: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInterfaceDelegate(IntPtr thisPtr, ref Guid iid, out IntPtr ppv);

    /// <summary>
    /// Ensures the staging texture exists with the correct dimensions.
    /// </summary>
    private void EnsureStagingTexture(int width, int height)
    {
        if (_stagingTexture != IntPtr.Zero)
            return;

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0
        };

        var hr = D3D11.CreateTexture2D(_d3dDevice, ref desc, IntPtr.Zero, out _stagingTexture);
        if (hr != 0)
        {
            throw new InvalidOperationException($"Failed to create staging texture: 0x{hr:X8}");
        }
    }

    /// <summary>
    /// Reads pixel data from the staging texture.
    /// </summary>
    /// <param name="width">Width of the frame.</param>
    /// <param name="height">Height of the frame.</param>
    /// <param name="frameId">Frame ID for performance tracking.</param>
    /// <param name="captureStartTicks">High-precision timestamp at capture start.</param>
    private CapturedFrame? ReadStagingTexture(int width, int height, ulong frameId = 0, long captureStartTicks = 0)
    {
        if (_stagingTexture == IntPtr.Zero || _d3dContext == IntPtr.Zero)
            return null;

        var hr = D3D11.Map(_d3dContext, _stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, out var mappedResource);
        if (hr != 0)
        {
            Debug.WriteLine($"Failed to map staging texture: 0x{hr:X8}");
            return null;
        }

        try
        {
            int stride = width * 4;
            int size = stride * height;
            var data = new byte[size];

            // Copy row by row (handles pitch differences)
            var srcPtr = mappedResource.pData;
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(srcPtr + row * (int)mappedResource.RowPitch, data, row * stride, stride);
            }

            return new CapturedFrame
            {
                Data = data,
                Width = width,
                Height = height,
                Stride = stride,
                Timestamp = DateTime.UtcNow,
                FrameId = frameId,
                CaptureStartTicks = captureStartTicks
            };
        }
        finally
        {
            D3D11.Unmap(_d3dContext, _stagingTexture, 0);
        }
    }

    private void OnFallbackFrameCaptured(object? sender, CapturedFrame e)
    {
        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = e;
        }

        FrameCaptured?.Invoke(this, e);
    }

    public void StopCapture()
    {
        if (!IsCapturing)
            return;

        if (_useWgc)
        {
            try
            {
                _captureSession?.Dispose();
                _captureSession = null;

                if (_framePool != null)
                {
                    _framePool.FrameArrived -= OnFrameArrived;
                    _framePool.Dispose();
                    _framePool = null;
                }

                _captureItem = null;
                Logger.Log("WindowsCaptureService: WGC capture stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping WGC capture: {ex.Message}");
            }
        }
        else
        {
            _fallbackService.FrameCaptured -= OnFallbackFrameCaptured;
            _fallbackService.StopCapture();
        }

        IsCapturing = false;
    }

    public CapturedFrame? GetLatestFrame()
    {
        lock (_frameLock)
        {
            return _latestFrame;
        }
    }

    /// <summary>
    /// Captures a single frame immediately.
    /// Uses the GDI fallback service for synchronous capture.
    /// </summary>
    public CapturedFrame? CaptureFrame()
    {
        // For synchronous single-shot capture, use the GDI fallback
        // WGC uses an async frame pool model that doesn't support sync capture well
        return _fallbackService.CaptureFrame();
    }

    private void CleanupWgcResources()
    {
        _captureSession?.Dispose();
        _captureSession = null;

        if (_framePool != null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }

        _captureItem = null;

        if (_stagingTexture != IntPtr.Zero)
        {
            Marshal.Release(_stagingTexture);
            _stagingTexture = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopCapture();
        CleanupWgcResources();

        _winrtDevice?.Dispose();
        _winrtDevice = null;

        if (_d3dContext != IntPtr.Zero)
        {
            Marshal.Release(_d3dContext);
            _d3dContext = IntPtr.Zero;
        }

        if (_d3dDevice != IntPtr.Zero)
        {
            Marshal.Release(_d3dDevice);
            _d3dDevice = IntPtr.Zero;
        }

        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _fallbackService.Dispose();
        GC.SuppressFinalize(this);
    }
}

#region Direct3D11 Interop

/// <summary>
/// Interface for accessing Direct3D/DXGI interfaces from WinRT objects.
/// </summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}

/// <summary>
/// Marker interface for ID3D11Texture2D GUID.
/// </summary>
[ComImport]
[Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11Texture2D { }

/// <summary>
/// Marker interface for IDXGIDevice GUID.
/// </summary>
[ComImport]
[Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIDevice { }

/// <summary>
/// Interop factory for creating GraphicsCaptureItem from native handles.
/// </summary>
internal static class GraphicsCaptureItemInterop
{
    private static IGraphicsCaptureItemInterop? _factory;
    private static bool _initialized;
    private static string? _initError;

    public static IGraphicsCaptureItemInterop? Factory
    {
        get
        {
            if (!_initialized)
            {
                _initialized = true;
                try
                {
                    _factory = CreateFactory();
                }
                catch (Exception ex)
                {
                    _initError = ex.Message;
                    Logger.Warn($"GraphicsCaptureItemInterop: Failed to create factory - {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Logger.Warn($"GraphicsCaptureItemInterop: Inner exception - {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                }
            }
            return _factory;
        }
    }

    public static string? InitializationError => _initError;

    private static IGraphicsCaptureItemInterop CreateFactory()
    {
        var iid = typeof(IGraphicsCaptureItemInterop).GUID;
        Logger.Log($"GraphicsCaptureItemInterop: Calling RoGetActivationFactory for Windows.Graphics.Capture.GraphicsCaptureItem");

        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        IntPtr hString = IntPtr.Zero;

        try
        {
            // Create HSTRING manually (MarshalAs(UnmanagedType.HString) doesn't work in .NET Core)
            var hr = WindowsCreateString(className, (uint)className.Length, out hString);
            if (hr != 0)
            {
                throw new InvalidOperationException($"WindowsCreateString failed with HRESULT 0x{hr:X8}");
            }

            hr = RoGetActivationFactory(hString, ref iid, out var factory);
            if (hr != 0)
            {
                throw new InvalidOperationException($"RoGetActivationFactory failed with HRESULT 0x{hr:X8}");
            }

            Logger.Log("GraphicsCaptureItemInterop: Factory created successfully");
            return (IGraphicsCaptureItemInterop)factory;
        }
        finally
        {
            // Clean up HSTRING
            if (hString != IntPtr.Zero)
            {
                WindowsDeleteString(hString);
            }
        }
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);
}

/// <summary>
/// COM interface for creating GraphicsCaptureItem from window or monitor handles.
/// Uses proper WinRT interop signature with riid and out pointer.
/// </summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(
        [In] IntPtr window,
        [In] ref Guid iid,
        out IntPtr result);

    int CreateForMonitor(
        [In] IntPtr monitor,
        [In] ref Guid iid,
        out IntPtr result);
}

/// <summary>
/// Direct3D11 P/Invoke declarations.
/// </summary>
internal static class D3D11
{
    public const uint D3D11_SDK_VERSION = 7;

    [DllImport("d3d11.dll", PreserveSig = true)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        D3D_DRIVER_TYPE DriverType,
        IntPtr Software,
        D3D11_CREATE_DEVICE_FLAG Flags,
        [MarshalAs(UnmanagedType.LPArray)] D3D_FEATURE_LEVEL[] pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out D3D_FEATURE_LEVEL pFeatureLevel,
        out IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", PreserveSig = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    // ID3D11Device::CreateTexture2D
    public static int CreateTexture2D(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var createTexture2D = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size); // vtable index 5
        var func = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(createTexture2D);
        return func(device, ref desc, initialData, out texture);
    }

    // ID3D11DeviceContext::CopyResource
    public static void CopyResource(IntPtr context, IntPtr dst, IntPtr src)
    {
        var vtable = Marshal.ReadIntPtr(context);
        var copyResource = Marshal.ReadIntPtr(vtable, 47 * IntPtr.Size); // vtable index 47
        var func = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(copyResource);
        func(context, dst, src);
    }

    // ID3D11DeviceContext::Map
    public static int Map(IntPtr context, IntPtr resource, uint subresource, D3D11_MAP mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mappedResource)
    {
        var vtable = Marshal.ReadIntPtr(context);
        var map = Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size); // vtable index 14
        var func = Marshal.GetDelegateForFunctionPointer<MapDelegate>(map);
        return func(context, resource, subresource, mapType, mapFlags, out mappedResource);
    }

    // ID3D11DeviceContext::Unmap
    public static void Unmap(IntPtr context, IntPtr resource, uint subresource)
    {
        var vtable = Marshal.ReadIntPtr(context);
        var unmap = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size); // vtable index 15
        var func = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(unmap);
        func(context, resource, subresource);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(IntPtr context, IntPtr dst, IntPtr src);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(IntPtr context, IntPtr resource, uint subresource, D3D11_MAP mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mappedResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(IntPtr context, IntPtr resource, uint subresource);
}

internal enum D3D_DRIVER_TYPE
{
    D3D_DRIVER_TYPE_UNKNOWN = 0,
    D3D_DRIVER_TYPE_HARDWARE = 1,
    D3D_DRIVER_TYPE_REFERENCE = 2,
    D3D_DRIVER_TYPE_NULL = 3,
    D3D_DRIVER_TYPE_SOFTWARE = 4,
    D3D_DRIVER_TYPE_WARP = 5
}

internal enum D3D_FEATURE_LEVEL
{
    D3D_FEATURE_LEVEL_9_1 = 0x9100,
    D3D_FEATURE_LEVEL_9_2 = 0x9200,
    D3D_FEATURE_LEVEL_9_3 = 0x9300,
    D3D_FEATURE_LEVEL_10_0 = 0xa000,
    D3D_FEATURE_LEVEL_10_1 = 0xa100,
    D3D_FEATURE_LEVEL_11_0 = 0xb000,
    D3D_FEATURE_LEVEL_11_1 = 0xb100,
    D3D_FEATURE_LEVEL_12_0 = 0xc000,
    D3D_FEATURE_LEVEL_12_1 = 0xc100
}

[Flags]
internal enum D3D11_CREATE_DEVICE_FLAG : uint
{
    D3D11_CREATE_DEVICE_SINGLETHREADED = 0x1,
    D3D11_CREATE_DEVICE_DEBUG = 0x2,
    D3D11_CREATE_DEVICE_SWITCH_TO_REF = 0x4,
    D3D11_CREATE_DEVICE_PREVENT_INTERNAL_THREADING_OPTIMIZATIONS = 0x8,
    D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20,
    D3D11_CREATE_DEVICE_DEBUGGABLE = 0x40,
    D3D11_CREATE_DEVICE_PREVENT_ALTERING_LAYER_SETTINGS_FROM_REGISTRY = 0x80,
    D3D11_CREATE_DEVICE_DISABLE_GPU_TIMEOUT = 0x100,
    D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800
}

internal enum D3D11_USAGE
{
    D3D11_USAGE_DEFAULT = 0,
    D3D11_USAGE_IMMUTABLE = 1,
    D3D11_USAGE_DYNAMIC = 2,
    D3D11_USAGE_STAGING = 3
}

[Flags]
internal enum D3D11_CPU_ACCESS_FLAG : uint
{
    D3D11_CPU_ACCESS_WRITE = 0x10000,
    D3D11_CPU_ACCESS_READ = 0x20000
}

internal enum D3D11_MAP
{
    D3D11_MAP_READ = 1,
    D3D11_MAP_WRITE = 2,
    D3D11_MAP_READ_WRITE = 3,
    D3D11_MAP_WRITE_DISCARD = 4,
    D3D11_MAP_WRITE_NO_OVERWRITE = 5
}

internal enum DXGI_FORMAT
{
    DXGI_FORMAT_UNKNOWN = 0,
    DXGI_FORMAT_R8G8B8A8_UNORM = 28,
    DXGI_FORMAT_B8G8R8A8_UNORM = 87
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SAMPLE_DESC
{
    public uint Count;
    public uint Quality;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_TEXTURE2D_DESC
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public DXGI_FORMAT Format;
    public DXGI_SAMPLE_DESC SampleDesc;
    public D3D11_USAGE Usage;
    public uint BindFlags;
    public D3D11_CPU_ACCESS_FLAG CPUAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_MAPPED_SUBRESOURCE
{
    public IntPtr pData;
    public uint RowPitch;
    public uint DepthPitch;
}

#endregion
