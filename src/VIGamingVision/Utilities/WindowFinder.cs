using System.Text;
using VIGamingVision.Native;

namespace VIGamingVision.Utilities;

/// <summary>
/// Utility for finding windows by title.
/// </summary>
public static class WindowFinder
{
    /// <summary>
    /// Finds a window by partial title match.
    /// </summary>
    /// <param name="partialTitle">Partial window title to search for (case-insensitive).</param>
    /// <returns>Window handle or IntPtr.Zero if not found.</returns>
    public static IntPtr FindWindowByTitle(string partialTitle)
    {
        IntPtr foundWindow = IntPtr.Zero;
        string searchTitle = partialTitle.ToLowerInvariant();

        User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title))
                return true;

            if (title.ToLowerInvariant().Contains(searchTitle))
            {
                foundWindow = hWnd;
                return false; // Stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        return foundWindow;
    }

    /// <summary>
    /// Gets all visible windows with titles.
    /// </summary>
    public static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        User32.EnumWindows((hWnd, lParam) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(title))
                return true;

            User32.GetWindowRect(hWnd, out var rect);

            // Skip windows with no size
            if (rect.Width <= 0 || rect.Height <= 0)
                return true;

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                Bounds = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height)
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Gets the title of a window.
    /// </summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = User32.GetWindowTextLengthW(hWnd);
        if (length == 0)
            return string.Empty;

        var buffer = new StringBuilder(length + 1);
        User32.GetWindowTextW(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    /// <summary>
    /// Gets the bounds of a window.
    /// </summary>
    public static System.Drawing.Rectangle GetWindowBounds(IntPtr hWnd)
    {
        User32.GetWindowRect(hWnd, out var rect);
        return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
    }
}

/// <summary>
/// Information about a window.
/// </summary>
public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public System.Drawing.Rectangle Bounds { get; set; }
}
