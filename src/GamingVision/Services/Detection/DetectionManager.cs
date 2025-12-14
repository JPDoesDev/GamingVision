using System.Diagnostics;
using System.IO;
using GamingVision.Models;
using GamingVision.Services.ScreenCapture;

namespace GamingVision.Services.Detection;

/// <summary>
/// Manages the detection pipeline, coordinating capture and inference.
/// </summary>
public class DetectionManager : IDisposable
{
    private readonly IDetectionService _detectionService;
    private GameProfile? _currentProfile;
    private bool _disposed;
    private DateTime _lastDetectionTime = DateTime.MinValue;
    private List<DetectedObject> _lastDetections = [];
    private readonly object _detectionLock = new();

    /// <summary>
    /// Gets whether the detection manager is currently running.
    /// </summary>
    public bool IsRunning => _detectionService?.IsReady ?? false;

    /// <summary>
    /// Gets the detection service for checking status.
    /// </summary>
    public IDetectionService DetectionService => _detectionService;

    /// <summary>
    /// Event raised when new detections are available.
    /// </summary>
    public event EventHandler<DetectionEventArgs>? DetectionsReady;

    /// <summary>
    /// Event raised when primary objects change (for auto-read).
    /// </summary>
    public event EventHandler<PrimaryObjectChangedEventArgs>? PrimaryObjectChanged;

    public DetectionManager()
    {
        _detectionService = new YoloDetectionService();
    }

    public DetectionManager(IDetectionService detectionService)
    {
        _detectionService = detectionService;
    }

    /// <summary>
    /// Initializes the detection manager for a game profile.
    /// </summary>
    /// <param name="profile">The game profile containing model path and settings.</param>
    /// <param name="modelsDirectory">Base directory for model files.</param>
    /// <returns>True if initialization succeeded.</returns>
    public async Task<bool> InitializeAsync(GameProfile profile, string modelsDirectory)
    {
        _currentProfile = profile;

        // Build model path
        var modelPath = Path.Combine(modelsDirectory, profile.ModelFile);

        if (!File.Exists(modelPath))
        {
            Debug.WriteLine($"Model file not found: {modelPath}");
            return false;
        }

        // Initialize detection service
        return await _detectionService.InitializeAsync(modelPath, useGpu: true);
    }

    /// <summary>
    /// Processes a captured frame and runs detection.
    /// </summary>
    /// <param name="frame">The captured frame to process.</param>
    /// <returns>List of detections, or empty list if detection is on cooldown.</returns>
    public async Task<List<DetectedObject>> ProcessFrameAsync(CapturedFrame frame)
    {
        if (!_detectionService.IsReady || _currentProfile == null)
            return [];

        var threshold = _currentProfile.Detection.ConfidenceThreshold;
        var detections = await _detectionService.DetectAsync(frame, threshold);

        // Store and process detections
        ProcessDetections(detections);

        return detections;
    }

    /// <summary>
    /// Processes detections to determine if primary objects changed.
    /// </summary>
    private void ProcessDetections(List<DetectedObject> detections)
    {
        if (_currentProfile == null) return;

        lock (_detectionLock)
        {
            // Filter by primary/secondary labels
            var primaryDetections = detections
                .Where(d => _currentProfile.PrimaryLabels.Contains(d.Label))
                .ToList();

            var secondaryDetections = detections
                .Where(d => _currentProfile.SecondaryLabels.Contains(d.Label))
                .ToList();

            // Raise detection event
            DetectionsReady?.Invoke(this, new DetectionEventArgs
            {
                AllDetections = detections,
                PrimaryDetections = primaryDetections,
                SecondaryDetections = secondaryDetections,
                Timestamp = DateTime.UtcNow
            });

            // Check if primary objects changed
            var previousPrimaryLabels = _lastDetections
                .Where(d => _currentProfile.PrimaryLabels.Contains(d.Label))
                .Select(d => d.Label)
                .OrderBy(l => l)
                .ToList();

            var currentPrimaryLabels = primaryDetections
                .Select(d => d.Label)
                .OrderBy(l => l)
                .ToList();

            bool primaryChanged = !previousPrimaryLabels.SequenceEqual(currentPrimaryLabels);

            // Check cooldown for auto-read
            var timeSinceLastDetection = DateTime.UtcNow - _lastDetectionTime;
            var cooldownMs = _currentProfile.Detection.AutoReadCooldown;

            if (primaryChanged && timeSinceLastDetection.TotalMilliseconds >= cooldownMs)
            {
                _lastDetectionTime = DateTime.UtcNow;

                // Sort by priority
                var sortedPrimary = SortByPriority(primaryDetections, _currentProfile.LabelPriority);

                PrimaryObjectChanged?.Invoke(this, new PrimaryObjectChangedEventArgs
                {
                    Detections = sortedPrimary,
                    Timestamp = DateTime.UtcNow
                });
            }

            _lastDetections = detections;
        }
    }

    /// <summary>
    /// Sorts detections by label priority.
    /// </summary>
    private static List<DetectedObject> SortByPriority(List<DetectedObject> detections, List<string> priorityOrder)
    {
        return detections
            .OrderBy(d =>
            {
                var index = priorityOrder.IndexOf(d.Label);
                return index >= 0 ? index : int.MaxValue;
            })
            .ThenByDescending(d => d.Confidence)
            .ToList();
    }

    /// <summary>
    /// Gets the highest priority secondary detection for on-demand reading.
    /// </summary>
    public DetectedObject? GetHighestPrioritySecondary()
    {
        if (_currentProfile == null) return null;

        lock (_detectionLock)
        {
            return _lastDetections
                .Where(d => _currentProfile.SecondaryLabels.Contains(d.Label))
                .OrderBy(d =>
                {
                    var index = _currentProfile.LabelPriority.IndexOf(d.Label);
                    return index >= 0 ? index : int.MaxValue;
                })
                .ThenByDescending(d => d.Confidence)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Gets all current primary detections sorted by priority.
    /// </summary>
    public List<DetectedObject> GetCurrentPrimaryDetections()
    {
        if (_currentProfile == null) return [];

        lock (_detectionLock)
        {
            return SortByPriority(
                _lastDetections.Where(d => _currentProfile.PrimaryLabels.Contains(d.Label)).ToList(),
                _currentProfile.LabelPriority);
        }
    }

    /// <summary>
    /// Gets all current secondary detections sorted by priority.
    /// </summary>
    public List<DetectedObject> GetCurrentSecondaryDetections()
    {
        if (_currentProfile == null) return [];

        lock (_detectionLock)
        {
            return SortByPriority(
                _lastDetections.Where(d => _currentProfile.SecondaryLabels.Contains(d.Label)).ToList(),
                _currentProfile.LabelPriority);
        }
    }

    /// <summary>
    /// Resets the auto-read cooldown timer.
    /// </summary>
    public void ResetCooldown()
    {
        _lastDetectionTime = DateTime.MinValue;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _detectionService.Dispose();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Event arguments for detection results.
/// </summary>
public class DetectionEventArgs : EventArgs
{
    public List<DetectedObject> AllDetections { get; init; } = [];
    public List<DetectedObject> PrimaryDetections { get; init; } = [];
    public List<DetectedObject> SecondaryDetections { get; init; } = [];
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Event arguments for primary object changes.
/// </summary>
public class PrimaryObjectChangedEventArgs : EventArgs
{
    public List<DetectedObject> Detections { get; init; } = [];
    public DateTime Timestamp { get; init; }
}
