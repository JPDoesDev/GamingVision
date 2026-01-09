namespace GamingVision.Models;

/// <summary>
/// Application-level configuration stored in app_settings.json.
/// </summary>
public class AppConfiguration
{
    /// <summary>
    /// Configuration file version for migration purposes.
    /// </summary>
    public string Version { get; set; } = "0.2.7";

    /// <summary>
    /// Currently selected game profile identifier.
    /// </summary>
    public string SelectedGame { get; set; } = "no_mans_sky";

    /// <summary>
    /// Enable GPU acceleration via DirectML.
    /// </summary>
    public bool UseDirectML { get; set; } = true;

    /// <summary>
    /// Start detection automatically when a game is selected.
    /// </summary>
    public bool AutoStartDetection { get; set; } = false;

    /// <summary>
    /// Minimize to system tray when closing the window.
    /// </summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>
    /// Start application minimized.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Enable debug logging to file.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Log file path (relative to app directory or absolute).
    /// </summary>
    public string LogFilePath { get; set; } = "logs/gamingvision.log";

    /// <summary>
    /// Check for updates on startup.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Enable screen reader (TTS) functionality when engine is running.
    /// </summary>
    public bool ScreenReaderEnabled { get; set; } = true;

    /// <summary>
    /// Enable overlay display when engine is running.
    /// </summary>
    public bool OverlayEnabled { get; set; } = false;

    /// <summary>
    /// Enable crosshair overlay when engine is running.
    /// </summary>
    public bool CrosshairEnabled { get; set; } = false;

    /// <summary>
    /// Creates default app configuration.
    /// </summary>
    public static AppConfiguration CreateDefault() => new()
    {
        Version = "0.2.7",
        SelectedGame = "no_mans_sky",
        UseDirectML = true,
        AutoStartDetection = false,
        MinimizeToTray = false,
        StartMinimized = false,
        EnableLogging = true,
        LogFilePath = "logs/gamingvision.log",
        CheckForUpdates = true,
        ScreenReaderEnabled = true,
        OverlayEnabled = false,
        CrosshairEnabled = false
    };
}
