namespace GamingVision.Models;

/// <summary>
/// Parameters for YOLO model training.
/// These settings are stored per-game in game_config.json.
/// </summary>
public class ModelTrainingParameters
{
    /// <summary>
    /// Number of complete passes through the training data.
    /// Range: 50-600. Default: 150.
    /// </summary>
    public int Epochs { get; set; } = 150;

    /// <summary>
    /// Training image resolution (square).
    /// Options: 640 (fast), 1280 (balanced), 1440 (best for UI text).
    /// </summary>
    public int ImageSize { get; set; } = 1440;

    /// <summary>
    /// Batch size or GPU memory fraction.
    /// Integer (4, 8, 16): Fixed batch size.
    /// Float 0-1 (0.70): Use X% of GPU memory.
    /// -1: Auto-detect optimal batch size.
    /// </summary>
    public double Batch { get; set; } = 0.70;

    /// <summary>
    /// Early stopping - stop if no improvement for N epochs.
    /// Range: 10-100. Default: 50.
    /// </summary>
    public int Patience { get; set; } = 50;

    /// <summary>
    /// Initial learning rate.
    /// Range: 0.0001 - 0.1. Default: 0.01 for SGD.
    /// </summary>
    public double LearningRate { get; set; } = 0.01;

    /// <summary>
    /// Device to use for training.
    /// Options: "cuda" (GPU), "cpu", or device ID.
    /// </summary>
    public string Device { get; set; } = "cuda";

    /// <summary>
    /// Number of data loader worker threads.
    /// Range: 0-16. Default: 8.
    /// </summary>
    public int Workers { get; set; } = 8;

    /// <summary>
    /// Whether to cache images in memory for faster training.
    /// </summary>
    public bool CacheImages { get; set; } = true;

    /// <summary>
    /// Whether to use Automatic Mixed Precision (FP16).
    /// Recommended for modern GPUs.
    /// </summary>
    public bool UseMixedPrecision { get; set; } = true;

    /// <summary>
    /// Creates a deep copy of these training parameters.
    /// </summary>
    public ModelTrainingParameters Clone() => new()
    {
        Epochs = Epochs,
        ImageSize = ImageSize,
        Batch = Batch,
        Patience = Patience,
        LearningRate = LearningRate,
        Device = Device,
        Workers = Workers,
        CacheImages = CacheImages,
        UseMixedPrecision = UseMixedPrecision
    };

    /// <summary>
    /// Returns default parameters optimized for fine-tuning.
    /// </summary>
    public static ModelTrainingParameters DefaultFineTune() => new()
    {
        Epochs = 150,
        ImageSize = 1440,
        Batch = 0.70,
        Patience = 50,
        LearningRate = 0.01,
        Device = "cuda",
        Workers = 8,
        CacheImages = true,
        UseMixedPrecision = true
    };

    /// <summary>
    /// Returns default parameters optimized for full retraining.
    /// </summary>
    public static ModelTrainingParameters DefaultFullRetrain() => new()
    {
        Epochs = 150,
        ImageSize = 1440,
        Batch = 0.70,
        Patience = 50,
        LearningRate = 0.01,
        Device = "cuda",
        Workers = 8,
        CacheImages = true,
        UseMixedPrecision = true
    };
}
