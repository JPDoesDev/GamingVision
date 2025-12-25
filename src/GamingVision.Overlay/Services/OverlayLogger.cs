using System.IO;

namespace GamingVision.Overlay.Services;

/// <summary>
/// Simple file logger for the overlay application.
/// Creates/empties log file on startup and writes all debug output to it.
/// </summary>
public static class OverlayLogger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "overlay_log.txt");

    private static readonly object _lock = new();
    private static bool _initialized;

    /// <summary>
    /// Initializes the logger, creating or emptying the log file.
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            try
            {
                // Create or empty the log file
                File.WriteAllText(LogPath, $"=== GamingVision Overlay Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n\n");
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Logs a message to the log file.
    /// </summary>
    public static void Log(string message)
    {
        lock (_lock)
        {
            try
            {
                if (!_initialized) Initialize();

                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                File.AppendAllText(LogPath, $"[{timestamp}] {message}\n");
            }
            catch
            {
                // Silently fail if logging fails
            }
        }
    }

    /// <summary>
    /// Logs a message with category prefix.
    /// </summary>
    public static void Log(string category, string message)
    {
        Log($"[{category}] {message}");
    }
}
