namespace GamingVision.Models;

/// <summary>
/// Configuration for the visual overlay feature.
/// Stored in game_config.json alongside other game settings.
/// </summary>
public class OverlaySettings
{
    /// <summary>
    /// Whether the overlay is enabled by default when starting.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Confidence threshold for overlay display (0.0-1.0).
    /// Independent from main app's detection threshold.
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Hotkey to toggle overlay visibility (e.g., "Alt+O").
    /// </summary>
    public string ToggleHotkey { get; set; } = "Alt+O";

    /// <summary>
    /// Overlay groups defining how different labels are visualized.
    /// </summary>
    public List<OverlayGroup> Groups { get; set; } = [];

    /// <summary>
    /// Creates a deep copy of these settings.
    /// </summary>
    public OverlaySettings Clone() => new()
    {
        Enabled = Enabled,
        ConfidenceThreshold = ConfidenceThreshold,
        ToggleHotkey = ToggleHotkey,
        Groups = Groups.Select(g => g.Clone()).ToList()
    };
}
