namespace GamingVision.Models;

/// <summary>
/// Hotkey configuration for a game profile.
/// </summary>
public class HotkeySettings
{
    /// <summary>
    /// Hotkey to force-read primary object (e.g., "Alt+1").
    /// </summary>
    public string ReadPrimary { get; set; } = "Alt+1";

    /// <summary>
    /// Hotkey to read secondary object (e.g., "Alt+2").
    /// </summary>
    public string ReadSecondary { get; set; } = "Alt+2";

    /// <summary>
    /// Hotkey to read tertiary object (e.g., "Alt+3").
    /// </summary>
    public string ReadTertiary { get; set; } = "Alt+3";

    /// <summary>
    /// Hotkey to stop current speech and clear queue (e.g., "Alt+4").
    /// </summary>
    public string StopReading { get; set; } = "Alt+4";

    /// <summary>
    /// Hotkey to pause/resume detection (e.g., "Alt+5").
    /// </summary>
    public string ToggleDetection { get; set; } = "Alt+5";

    /// <summary>
    /// Hotkey to exit the application (e.g., "Alt+Q").
    /// </summary>
    public string Quit { get; set; } = "Alt+Q";

    /// <summary>
    /// Creates a deep copy of this instance.
    /// </summary>
    public HotkeySettings Clone() => new()
    {
        ReadPrimary = ReadPrimary,
        ReadSecondary = ReadSecondary,
        ReadTertiary = ReadTertiary,
        StopReading = StopReading,
        ToggleDetection = ToggleDetection,
        Quit = Quit
    };
}
