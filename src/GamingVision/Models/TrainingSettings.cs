using System.IO;

namespace GamingVision.Models;

/// <summary>
/// Training data collection settings for a game profile.
/// </summary>
public class TrainingSettings
{
    /// <summary>
    /// Default root path for training data.
    /// </summary>
    public const string DefaultTrainingRootPath = @"C:\GamingVision";

    /// <summary>
    /// Whether training screenshot capture is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the training data folder (legacy, for backward compatibility).
    /// If empty or null, defaults to "training_data/{gameId}".
    /// </summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>
    /// Root path for all training data folders.
    /// Contains New_Training_Data, Base_Training_Data, and training_stats subfolders.
    /// </summary>
    public string TrainingRootPath { get; set; } = DefaultTrainingRootPath;

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
    /// Model training parameters for this game.
    /// Saved per-game to allow different settings for different games.
    /// </summary>
    public ModelTrainingParameters? TrainingParameters { get; set; }

    /// <summary>
    /// Gets the path to New Training Data folder for a specific game.
    /// New captures go here for annotation before training.
    /// </summary>
    public string GetNewTrainingDataPath(string gameId)
    {
        return Path.Combine(TrainingRootPath, "New_Training_Data", gameId);
    }

    /// <summary>
    /// Gets the path to Base Training Data folder for a specific game.
    /// Accumulated dataset used for full retraining.
    /// </summary>
    public string GetBaseTrainingDataPath(string gameId)
    {
        return Path.Combine(TrainingRootPath, "Base_Training_Data", gameId);
    }

    /// <summary>
    /// Gets the path to training statistics folder for a specific game.
    /// Training results and metrics are saved here.
    /// </summary>
    public string GetTrainingStatsPath(string gameId)
    {
        return Path.Combine(TrainingRootPath, "training_stats", gameId);
    }

    /// <summary>
    /// Creates a deep copy of these training settings.
    /// </summary>
    public TrainingSettings Clone() => new()
    {
        Enabled = Enabled,
        DataPath = DataPath,
        TrainingRootPath = TrainingRootPath,
        CaptureHotkey = CaptureHotkey,
        AnnotationConfidenceThreshold = AnnotationConfidenceThreshold,
        TrainingParameters = TrainingParameters?.Clone()
    };
}
