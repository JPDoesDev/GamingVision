using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Windows;

/// <summary>
/// Lightweight transparent overlay window for drawing a crosshair.
/// </summary>
public partial class CrosshairWindow : Window
{
    /// <summary>
    /// The DPI scale factor for this window.
    /// </summary>
    public double DpiScale { get; private set; } = 1.0;

    /// <summary>
    /// Physical pixel width of the display area.
    /// </summary>
    public int DisplayWidth { get; private set; }

    /// <summary>
    /// Physical pixel height of the display area.
    /// </summary>
    public int DisplayHeight { get; private set; }

    public CrosshairWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the Canvas element for drawing.
    /// </summary>
    public System.Windows.Controls.Canvas Canvas => CrosshairCanvas;

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
    /// Positions the crosshair window to cover a specific window.
    /// </summary>
    public void PositionOverWindow(IntPtr targetHwnd)
    {
        if (User32.GetWindowRect(targetHwnd, out var rect))
        {
            // Get DPI scale
            var presentationSource = PresentationSource.FromVisual(this);
            if (presentationSource?.CompositionTarget != null)
            {
                DpiScale = presentationSource.CompositionTarget.TransformToDevice.M11;
            }
            else
            {
                DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            }

            DisplayWidth = rect.Width;
            DisplayHeight = rect.Height;

            // Convert to DIPs
            double dipLeft = rect.Left / DpiScale;
            double dipTop = rect.Top / DpiScale;
            double dipWidth = rect.Width / DpiScale;
            double dipHeight = rect.Height / DpiScale;

            WindowState = WindowState.Normal;
            Left = dipLeft;
            Top = dipTop;
            Width = dipWidth;
            Height = dipHeight;

            Logger.Log($"Crosshair positioned over window: {rect.Width}x{rect.Height}, DPI={DpiScale:F2}");
        }
    }

    /// <summary>
    /// Positions the crosshair window to cover a specific monitor.
    /// </summary>
    public void PositionOverMonitor(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
        {
            monitorIndex = 0;
        }

        var screen = screens[monitorIndex];
        var bounds = screen.Bounds;

        DisplayWidth = bounds.Width;
        DisplayHeight = bounds.Height;

        // Get DPI scale
        var presentationSource = PresentationSource.FromVisual(this);
        if (presentationSource?.CompositionTarget != null)
        {
            DpiScale = presentationSource.CompositionTarget.TransformToDevice.M11;
        }
        else
        {
            DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        // Convert to DIPs
        double dipLeft = bounds.Left / DpiScale;
        double dipTop = bounds.Top / DpiScale;
        double dipWidth = bounds.Width / DpiScale;
        double dipHeight = bounds.Height / DpiScale;

        WindowState = WindowState.Normal;
        Left = dipLeft;
        Top = dipTop;
        Width = dipWidth;
        Height = dipHeight;

        Logger.Log($"Crosshair positioned on monitor {monitorIndex}: {bounds.Width}x{bounds.Height}, DPI={DpiScale:F2}");
    }
}
