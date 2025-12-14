namespace VIGamingVision.Models;

/// <summary>
/// Application-wide accessibility settings.
/// </summary>
public class AccessibilitySettings
{
    /// <summary>
    /// Enable high contrast UI theme.
    /// </summary>
    public bool HighContrast { get; set; } = false;

    /// <summary>
    /// Enable larger text in the launcher UI.
    /// </summary>
    public bool LargeText { get; set; } = false;

    /// <summary>
    /// Creates a deep copy of this instance.
    /// </summary>
    public AccessibilitySettings Clone() => new()
    {
        HighContrast = HighContrast,
        LargeText = LargeText
    };
}
