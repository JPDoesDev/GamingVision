using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Windows;

/// <summary>
/// Transparent overlay window for drawing detection bounding boxes.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// The DPI scale factor for this window (e.g., 1.25 for 125% scaling).
    /// Used to convert physical pixel coordinates to WPF DIPs.
    /// </summary>
    public double DpiScale { get; private set; } = 1.0;

    /// <summary>
    /// The actual physical pixel dimensions of the capture area.
    /// </summary>
    public int CaptureWidth { get; private set; }
    public int CaptureHeight { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the Canvas element for drawing.
    /// </summary>
    public System.Windows.Controls.Canvas Canvas => OverlayCanvas;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MakeClickThrough();
    }

    /// <summary>
    /// Makes the window click-through by setting WS_EX_TRANSPARENT and WS_EX_LAYERED.
    /// </summary>
    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
            extendedStyle | User32.WS_EX_TRANSPARENT | User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Positions the overlay window to cover a specific window.
    /// </summary>
    public void PositionOverWindow(IntPtr targetHwnd)
    {
        if (User32.GetWindowRect(targetHwnd, out var rect))
        {
            Left = rect.Left;
            Top = rect.Top;
            Width = rect.Width;
            Height = rect.Height;
            WindowState = WindowState.Normal;
        }
    }

    /// <summary>
    /// Positions the overlay window to cover a specific monitor.
    /// Calculates DPI scaling to ensure detection coordinates align correctly.
    /// </summary>
    public void PositionOverMonitor(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
        {
            monitorIndex = 0; // Fallback to primary
        }

        var screen = screens[monitorIndex];
        var bounds = screen.Bounds;

        // Store the physical pixel dimensions for coordinate scaling
        CaptureWidth = bounds.Width;
        CaptureHeight = bounds.Height;

        // Get DPI scale factor for this window
        // PresentationSource is available after window is shown, so we may need to update later
        var presentationSource = PresentationSource.FromVisual(this);
        if (presentationSource?.CompositionTarget != null)
        {
            DpiScale = presentationSource.CompositionTarget.TransformToDevice.M11;
        }
        else
        {
            // Fallback: use system DPI
            DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        // Convert physical pixel bounds to WPF DIPs for correct window sizing
        // Screen.Bounds returns physical pixels, but WPF expects DIPs
        double dipLeft = bounds.Left / DpiScale;
        double dipTop = bounds.Top / DpiScale;
        double dipWidth = bounds.Width / DpiScale;
        double dipHeight = bounds.Height / DpiScale;

        // Must set Normal state before setting position, otherwise WPF ignores Left/Top
        WindowState = WindowState.Normal;
        Left = dipLeft;
        Top = dipTop;
        Width = dipWidth;
        Height = dipHeight;

        Logger.Log("Overlay", $"Positioned on monitor {monitorIndex}: physical=({bounds.Left},{bounds.Top}) {bounds.Width}x{bounds.Height}, DPI={DpiScale:F2} ({DpiScale * 100:F0}%), DIPs=({dipLeft:F0},{dipTop:F0}) {dipWidth:F0}x{dipHeight:F0}");
    }

    /// <summary>
    /// Resets the overlay to cover the entire primary screen.
    /// </summary>
    public void PositionFullScreen()
    {
        WindowState = WindowState.Maximized;
    }
}
