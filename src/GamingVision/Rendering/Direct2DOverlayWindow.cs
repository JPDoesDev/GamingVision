using System.Runtime.InteropServices;
using System.Windows.Forms;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Rendering;

/// <summary>
/// Native Win32 overlay window with DWM per-pixel alpha transparency.
/// Replaces both OverlayWindow and CrosshairWindow with a single window.
/// Uses DwmExtendFrameIntoClientArea for transparency instead of WS_EX_LAYERED,
/// enabling Direct2D HwndRenderTarget rendering with premultiplied alpha.
/// </summary>
public class Direct2DOverlayWindow : IDisposable
{
    private IntPtr _hwnd;
    private User32.WndProcDelegate? _wndProcDelegate; // prevent GC collection
    private bool _disposed;

    /// <summary>Native window handle.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>DPI scale factor for the monitor this window covers.</summary>
    public double DpiScale { get; private set; } = 1.0;

    /// <summary>Physical pixel width of the overlay area.</summary>
    public int PhysicalWidth { get; private set; }

    /// <summary>Physical pixel height of the overlay area.</summary>
    public int PhysicalHeight { get; private set; }

    /// <summary>Whether the window is currently visible.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Creates the native overlay window with click-through and DWM transparency.
    /// </summary>
    public void Create()
    {
        _wndProcDelegate = WndProc;

        var hInstance = User32.GetModuleHandle(null);
        var className = $"GVOverlay_{Environment.TickCount64}";

        var wndClass = new User32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            lpszClassName = className,
            hbrBackground = IntPtr.Zero
        };

        var atom = User32.RegisterClassEx(ref wndClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Error($"RegisterClassEx failed: 0x{error:X8}");
            return;
        }

        // Click-through requires WS_EX_LAYERED + WS_EX_TRANSPARENT together.
        // WS_EX_LAYERED enables the layered window hit-test path that respects WS_EX_TRANSPARENT.
        // DWM still handles visual transparency via DwmExtendFrameIntoClientArea.
        uint exStyle =
            (uint)User32.WS_EX_LAYERED |
            (uint)User32.WS_EX_TRANSPARENT |
            User32.WS_EX_TOPMOST |
            (uint)User32.WS_EX_TOOLWINDOW |
            User32.WS_EX_NOACTIVATE;

        _hwnd = User32.CreateWindowEx(
            exStyle,
            className,
            "GamingVision Overlay",
            User32.WS_POPUP,
            0, 0, 1, 1, // repositioned by PositionOverMonitor
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Error($"CreateWindowEx failed: 0x{error:X8}");
            return;
        }

        // Make the layered window fully opaque at the Win32 level.
        // Visual per-pixel transparency is handled by DWM + Direct2D premultiplied alpha.
        User32.SetLayeredWindowAttributes(_hwnd, 0, 255, User32.LWA_ALPHA);

        // Extend DWM frame over entire client area for per-pixel alpha
        var margins = new Dwmapi.MARGINS
        {
            LeftWidth = -1,
            RightWidth = -1,
            TopHeight = -1,
            BottomHeight = -1
        };
        Dwmapi.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        Logger.Log("D2DOverlay", "Window created");
    }

    /// <summary>Shows the overlay window.</summary>
    public void Show()
    {
        if (_hwnd == IntPtr.Zero) return;
        User32.ShowWindow(_hwnd, User32.SW_SHOW);
        IsVisible = true;
    }

    /// <summary>Hides the overlay window.</summary>
    public void Hide()
    {
        if (_hwnd == IntPtr.Zero) return;
        User32.ShowWindow(_hwnd, User32.SW_HIDE);
        IsVisible = false;
    }

    /// <summary>
    /// Positions the overlay window to cover a specific monitor.
    /// Operates in physical pixel coordinates (no DPI conversion needed).
    /// </summary>
    public void PositionOverMonitor(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
            monitorIndex = 0;

        var screen = screens[monitorIndex];
        var bounds = screen.Bounds;

        PhysicalWidth = bounds.Width;
        PhysicalHeight = bounds.Height;

        // Native window uses physical pixel coordinates directly
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            User32.SWP_NOACTIVATE);

        // Re-apply DWM margins after resize
        var margins = new Dwmapi.MARGINS
        {
            LeftWidth = -1,
            RightWidth = -1,
            TopHeight = -1,
            BottomHeight = -1
        };
        Dwmapi.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        // Calculate DPI scale for this monitor
        DpiScale = screen.Bounds.Width > 0
            ? screen.Bounds.Width / (double)screen.WorkingArea.Width
            : 1.0;

        // More accurate DPI: use the DC approach
        var hdc = User32.GetDC(_hwnd);
        if (hdc != IntPtr.Zero)
        {
            var dpiX = GetDeviceCaps(hdc, 88); // LOGPIXELSX
            DpiScale = dpiX / 96.0;
            User32.ReleaseDC(_hwnd, hdc);
        }

        Logger.Log("D2DOverlay", $"Positioned on monitor {monitorIndex}: {bounds.Width}x{bounds.Height} at ({bounds.Left},{bounds.Top}), DPI={DpiScale:F2}");
    }

    /// <summary>Destroys the native window.</summary>
    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            User32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        IsVisible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _wndProcDelegate = null;
        GC.SuppressFinalize(this);
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
}
