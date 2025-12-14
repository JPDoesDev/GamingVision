using VIGamingVision.Models;
using VIGamingVision.Services.ScreenCapture;

namespace VIGamingVision.Services.Detection;

/// <summary>
/// Interface for object detection services.
/// </summary>
public interface IDetectionService : IDisposable
{
    /// <summary>
    /// Gets whether the detection service is ready to process frames.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Gets the labels/class names supported by the loaded model.
    /// </summary>
    IReadOnlyList<string> Labels { get; }

    /// <summary>
    /// Initializes the detection service with a model file.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file.</param>
    /// <param name="useGpu">Whether to use GPU acceleration.</param>
    /// <returns>True if initialization succeeded.</returns>
    Task<bool> InitializeAsync(string modelPath, bool useGpu = true);

    /// <summary>
    /// Runs object detection on a captured frame.
    /// </summary>
    /// <param name="frame">The captured frame to process.</param>
    /// <param name="confidenceThreshold">Minimum confidence threshold.</param>
    /// <returns>List of detected objects.</returns>
    Task<List<DetectedObject>> DetectAsync(CapturedFrame frame, float confidenceThreshold = 0.5f);

    /// <summary>
    /// Gets information about the current execution provider (CPU, GPU, etc.).
    /// </summary>
    string ExecutionProvider { get; }
}

/// <summary>
/// Result of a detection operation with timing information.
/// </summary>
public class DetectionTimingResult
{
    /// <summary>
    /// List of detected objects.
    /// </summary>
    public List<DetectedObject> Detections { get; init; } = [];

    /// <summary>
    /// Time taken for inference in milliseconds.
    /// </summary>
    public double InferenceTimeMs { get; init; }

    /// <summary>
    /// Time taken for preprocessing in milliseconds.
    /// </summary>
    public double PreprocessTimeMs { get; init; }

    /// <summary>
    /// Time taken for post-processing in milliseconds.
    /// </summary>
    public double PostprocessTimeMs { get; init; }

    /// <summary>
    /// Total time for the detection operation in milliseconds.
    /// </summary>
    public double TotalTimeMs => PreprocessTimeMs + InferenceTimeMs + PostprocessTimeMs;
}
