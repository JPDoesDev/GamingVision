using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Windows;

/// <summary>
/// Transparent overlay window for drawing detection bounding boxes.
/// </summary>
public partial class OverlayWindow : Window
{
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

        // Must set Normal state before setting position, otherwise WPF ignores Left/Top
        WindowState = WindowState.Normal;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        Logger.Log("Overlay", $"Positioned on monitor {monitorIndex}: ({bounds.Left},{bounds.Top}) {bounds.Width}x{bounds.Height}");
    }

    /// <summary>
    /// Resets the overlay to cover the entire primary screen.
    /// </summary>
    public void PositionFullScreen()
    {
        WindowState = WindowState.Maximized;
    }
}
