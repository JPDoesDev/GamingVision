using System.Runtime.InteropServices;

namespace GamingVision.Native;

/// <summary>
/// Desktop Window Manager P/Invoke declarations.
/// Used for per-pixel alpha transparency on the Direct2D overlay window.
/// </summary>
public static class Dwmapi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);
}
