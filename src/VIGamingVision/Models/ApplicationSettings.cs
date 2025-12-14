namespace VIGamingVision.Models;

/// <summary>
/// Application-wide settings shared across all games.
/// </summary>
public class ApplicationSettings
{
    /// <summary>
    /// Accessibility options for the launcher UI.
    /// </summary>
    public AccessibilitySettings Accessibility { get; set; } = new();

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
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// Log file path (relative to app directory or absolute).
    /// </summary>
    public string LogFilePath { get; set; } = "logs/vigamingvision.log";

    /// <summary>
    /// Check for updates on startup.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Creates a deep copy of this instance.
    /// </summary>
    public ApplicationSettings Clone() => new()
    {
        Accessibility = Accessibility.Clone(),
        UseDirectML = UseDirectML,
        AutoStartDetection = AutoStartDetection,
        MinimizeToTray = MinimizeToTray,
        StartMinimized = StartMinimized,
        EnableLogging = EnableLogging,
        LogFilePath = LogFilePath,
        CheckForUpdates = CheckForUpdates
    };
}
