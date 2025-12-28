namespace GamingVision.Models;

/// <summary>
/// Training data collection settings for a game profile.
/// </summary>
public class TrainingSettings
{
    /// <summary>
    /// Whether training screenshot capture is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the training data folder.
    /// If empty or null, defaults to "training_data/{gameId}".
    /// </summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>
    /// Hotkey for capturing a training screenshot.
    /// </summary>
    public string CaptureHotkey { get; set; } = "F1";

    /// <summary>
    /// Minimum confidence threshold for auto-annotation.
    /// Detections below this threshold won't be saved as annotations.
    /// </summary>
    public float AnnotationConfidenceThreshold { get; set; } = 0.1f;

    /// <summary>
    /// Creates a deep copy of these training settings.
    /// </summary>
    public TrainingSettings Clone() => new()
    {
        Enabled = Enabled,
        DataPath = DataPath,
        CaptureHotkey = CaptureHotkey,
        AnnotationConfidenceThreshold = AnnotationConfidenceThreshold
    };
}
