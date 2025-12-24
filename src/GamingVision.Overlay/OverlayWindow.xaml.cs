using System.Windows;
using System.Windows.Interop;
using GamingVision.Native;

namespace GamingVision.Overlay;

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
    /// Resets the overlay to cover the entire screen.
    /// </summary>
    public void PositionFullScreen()
    {
        WindowState = WindowState.Maximized;
    }
}
