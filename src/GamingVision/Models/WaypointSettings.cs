namespace GamingVision.Models;

/// <summary>
/// Waypoint tracking configuration for a game profile.
/// Waypoints are read on a timer, independent of primary auto-read.
/// </summary>
public class WaypointSettings
{
    /// <summary>
    /// Whether waypoint tracking is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The label to track as a waypoint (must match model labels).
    /// Only ONE label can be the waypoint.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Read mode: "read" for OCR+TTS, "sonar" for audio feedback (future).
    /// </summary>
    public string Mode { get; set; } = "read";

    /// <summary>
    /// Interval in seconds between waypoint reads (0.5 to 10.0).
    /// </summary>
    public float ReadIntervalSeconds { get; set; } = 2.0f;

    /// <summary>
    /// Creates a deep copy of these settings.
    /// </summary>
    public WaypointSettings Clone() => new()
    {
        Enabled = Enabled,
        Label = Label,
        Mode = Mode,
        ReadIntervalSeconds = ReadIntervalSeconds
    };
}
