namespace GamingVision.Models;

/// <summary>
/// Detection configuration for a game profile.
/// </summary>
public class DetectionSettings
{
    /// <summary>
    /// Milliseconds between auto-reads (default: 2000).
    /// </summary>
    public int AutoReadCooldown { get; set; } = 2000;

    /// <summary>
    /// Minimum confidence threshold for detections (0.0-1.0).
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>
    /// IOU threshold for Non-Maximum Suppression (0.0-1.0).
    /// Higher values allow more overlapping boxes.
    /// </summary>
    public float NmsThreshold { get; set; } = 0.45f;

    /// <summary>
    /// Maximum number of detections to process per frame.
    /// </summary>
    public int MaxDetections { get; set; } = 100;

    /// <summary>
    /// Target frames per second for detection loop.
    /// </summary>
    public int TargetFps { get; set; } = 10;

    /// <summary>
    /// Enable auto-read when primary objects are detected.
    /// </summary>
    public bool AutoReadEnabled { get; set; } = true;

    /// <summary>
    /// Only read objects that have changed since last read.
    /// </summary>
    public bool OnlyReadChanges { get; set; } = true;

    /// <summary>
    /// Creates a deep copy of this instance.
    /// </summary>
    public DetectionSettings Clone() => new()
    {
        AutoReadCooldown = AutoReadCooldown,
        ConfidenceThreshold = ConfidenceThreshold,
        NmsThreshold = NmsThreshold,
        MaxDetections = MaxDetections,
        TargetFps = TargetFps,
        AutoReadEnabled = AutoReadEnabled,
        OnlyReadChanges = OnlyReadChanges
    };
}
