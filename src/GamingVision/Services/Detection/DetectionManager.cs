using System.Diagnostics;
using System.IO;
using GamingVision.Models;
using GamingVision.Services.ScreenCapture;
using GamingVision.Utilities;

namespace GamingVision.Services.Detection;

/// <summary>
/// Manages the detection pipeline, coordinating capture and inference.
/// </summary>
public class DetectionManager : IDisposable
{
    private readonly IDetectionService _detectionService;
    private GameProfile? _currentProfile;
    private bool _disposed;
    private List<DetectedObject> _lastDetections = [];
    private readonly object _detectionLock = new();

    // Position tracking for auto-read (replaces cooldown-based approach)
    private readonly Dictionary<string, TrackedPosition> _previousPositions = new();
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private const float PositionChangeThreshold = 0.10f; // 10% position change triggers re-read

    // Tracking for label disappearance detection
    private readonly Dictionary<string, int> _framesSinceLastSeen = new();
    private readonly Dictionary<string, DetectedObject> _trackedObjects = new();
    private const int DefaultDisappearanceFrameThreshold = 5;

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

    /// <summary>
    /// Event raised when a tracked label disappears (not detected for several frames).
    /// Used to cancel ongoing TTS reads when user moves away from an object.
    /// </summary>
    public event EventHandler<LabelDisappearedEventArgs>? LabelDisappeared;

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
        {
            Logger.Warn($"ProcessFrameAsync: Early exit - service ready: {_detectionService.IsReady}, profile null: {_currentProfile == null}");
            return [];
        }

        // Use the lower manual read threshold for detection to capture all potential objects
        // Auto-read will filter more strictly, manual reads will use these results
        var threshold = _currentProfile.Detection.ConfidenceThreshold;
        var detections = await _detectionService.DetectAsync(frame, threshold);

        // DetectAsync returns null when inference was skipped (already running)
        // Only update stored detections when inference actually ran
        if (detections == null)
        {
            // Inference was skipped, don't overwrite previous detections
            return [];
        }

        // Store and process detections (inference actually ran)
        Logger.Log($"ProcessFrameAsync: DetectAsync returned {detections.Count} detections, calling ProcessDetections");
        ProcessDetections(detections, frame.Width, frame.Height);

        return detections;
    }

    /// <summary>
    /// Processes detections to determine if primary objects changed.
    /// Uses position-based change detection instead of cooldown.
    /// </summary>
    private void ProcessDetections(List<DetectedObject> detections, int frameWidth, int frameHeight)
    {
        if (_currentProfile == null)
        {
            Logger.Warn("ProcessDetections: _currentProfile is null, returning");
            return;
        }

        Logger.Log($"ProcessDetections: Storing {detections.Count} detections in _lastDetections");
        lock (_detectionLock)
        {
            // Check if any tracked labels have disappeared (for TTS cancellation)
            CheckTrackedLabelsDisappearance(detections);

            // Filter by primary/secondary labels (for general tracking)
            var primaryDetections = detections
                .Where(d => _currentProfile.PrimaryLabels.Contains(d.Label))
                .ToList();

            var secondaryDetections = detections
                .Where(d => _currentProfile.SecondaryLabels.Contains(d.Label))
                .ToList();

            var tertiaryDetections = detections
                .Where(d => _currentProfile.TertiaryLabels.Contains(d.Label))
                .ToList();

            // Filter primary detections by higher auto-read threshold for automatic reading
            var autoReadThreshold = _currentProfile.Detection.AutoReadConfidenceThreshold;

            // Exclude waypoint label from auto-read (it has its own timer)
            var waypointLabel = _currentProfile.Waypoint?.Enabled == true
                ? _currentProfile.Waypoint.Label
                : null;

            var autoReadPrimaryDetections = primaryDetections
                .Where(d => d.Confidence >= autoReadThreshold)
                .Where(d => string.IsNullOrEmpty(waypointLabel) || d.Label != waypointLabel)
                .ToList();

            // Raise detection event
            DetectionsReady?.Invoke(this, new DetectionEventArgs
            {
                AllDetections = detections,
                PrimaryDetections = primaryDetections,
                SecondaryDetections = secondaryDetections,
                TertiaryDetections = tertiaryDetections,
                Timestamp = DateTime.UtcNow
            });

            // Position-based auto-read: detect new labels or significant position changes
            var detectionsToRead = new List<DetectedObject>();
            var currentPositions = new Dictionary<string, TrackedPosition>();

            // Build current positions map (use highest confidence detection per label)
            foreach (var det in autoReadPrimaryDetections.OrderByDescending(d => d.Confidence))
            {
                if (!currentPositions.ContainsKey(det.Label))
                {
                    currentPositions[det.Label] = new TrackedPosition
                    {
                        Label = det.Label,
                        CenterX = det.CenterX,
                        CenterY = det.CenterY,
                        FrameWidth = frameWidth,
                        FrameHeight = frameHeight
                    };
                }
            }

            var previousLabels = _previousPositions.Keys.ToHashSet();
            var currentLabels = currentPositions.Keys.ToHashSet();

            // Case 1: NEW labels that weren't in previous frame
            foreach (var label in currentLabels.Except(previousLabels))
            {
                var det = autoReadPrimaryDetections.First(d => d.Label == label);
                detectionsToRead.Add(det);
                Logger.Log($"AutoRead: New label appeared: {label}");
            }

            // Case 2: EXISTING labels with significant position change (>10% in X or Y)
            foreach (var label in currentLabels.Intersect(previousLabels))
            {
                var prevPos = _previousPositions[label];
                var currPos = currentPositions[label];

                if (currPos.HasMovedSignificantly(prevPos, PositionChangeThreshold))
                {
                    var det = autoReadPrimaryDetections.First(d => d.Label == label);
                    detectionsToRead.Add(det);
                    Logger.Log($"AutoRead: Label position changed significantly: {label}");
                }
            }

            // Fire event if there are detections to read
            if (detectionsToRead.Count > 0)
            {
                var sortedDetections = SortByPriority(detectionsToRead, _currentProfile.LabelPriority);

                PrimaryObjectChanged?.Invoke(this, new PrimaryObjectChangedEventArgs
                {
                    Detections = sortedDetections,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Update tracked positions for next frame
            _previousPositions.Clear();
            foreach (var kvp in currentPositions)
            {
                _previousPositions[kvp.Key] = kvp.Value;
            }

            _lastFrameWidth = frameWidth;
            _lastFrameHeight = frameHeight;
            _lastDetections = detections;
            Logger.LogDebug($"ProcessDetections: Stored {detections.Count} detections in _lastDetections");
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
        if (_currentProfile == null)
        {
            Logger.Warn("GetCurrentPrimaryDetections: No current profile");
            return [];
        }

        lock (_detectionLock)
        {
            Logger.Log($"GetCurrentPrimaryDetections: _lastDetections count = {_lastDetections.Count}");
            if (_lastDetections.Count > 0)
            {
                var labels = string.Join(", ", _lastDetections.Select(d => $"{d.Label}({d.Confidence:F2})"));
                Logger.Log($"GetCurrentPrimaryDetections: Detection labels = [{labels}]");
            }
            Logger.Log($"GetCurrentPrimaryDetections: PrimaryLabels = [{string.Join(", ", _currentProfile.PrimaryLabels)}]");

            var filtered = _lastDetections.Where(d => _currentProfile.PrimaryLabels.Contains(d.Label)).ToList();
            Logger.Log($"GetCurrentPrimaryDetections: After filtering = {filtered.Count} detections");

            return SortByPriority(filtered, _currentProfile.LabelPriority);
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
    /// Gets all current tertiary detections sorted by priority.
    /// </summary>
    public List<DetectedObject> GetCurrentTertiaryDetections()
    {
        if (_currentProfile == null) return [];

        lock (_detectionLock)
        {
            return SortByPriority(
                _lastDetections.Where(d => _currentProfile.TertiaryLabels.Contains(d.Label)).ToList(),
                _currentProfile.LabelPriority);
        }
    }

    /// <summary>
    /// Resets position tracking, causing the next detection to trigger auto-read.
    /// </summary>
    public void ResetPositionTracking()
    {
        lock (_detectionLock)
        {
            _previousPositions.Clear();
        }
    }

    /// <summary>
    /// Gets the current detection for a specific waypoint label.
    /// Used by the waypoint timer to read waypoints independently.
    /// </summary>
    public DetectedObject? GetWaypointDetection(string waypointLabel)
    {
        if (string.IsNullOrEmpty(waypointLabel))
            return null;

        lock (_detectionLock)
        {
            return _lastDetections.FirstOrDefault(d => d.Label == waypointLabel);
        }
    }

    /// <summary>
    /// Starts tracking a label for disappearance detection.
    /// Call this when starting to read an object so we can detect when user moves away.
    /// </summary>
    /// <param name="label">The label to track.</param>
    /// <param name="detection">The detection object being read.</param>
    public void StartTrackingLabel(string label, DetectedObject detection)
    {
        lock (_detectionLock)
        {
            _trackedObjects[label] = detection;
            _framesSinceLastSeen[label] = 0;
            Debug.WriteLine($"Started tracking label: {label}");
        }
    }

    /// <summary>
    /// Stops tracking a label (e.g., when TTS completes normally).
    /// </summary>
    /// <param name="label">The label to stop tracking.</param>
    public void StopTrackingLabel(string label)
    {
        lock (_detectionLock)
        {
            _trackedObjects.Remove(label);
            _framesSinceLastSeen.Remove(label);
            Debug.WriteLine($"Stopped tracking label: {label}");
        }
    }

    /// <summary>
    /// Stops tracking all labels.
    /// </summary>
    public void StopTrackingAllLabels()
    {
        lock (_detectionLock)
        {
            _trackedObjects.Clear();
            _framesSinceLastSeen.Clear();
        }
    }

    /// <summary>
    /// Checks if tracked labels have disappeared and raises events accordingly.
    /// </summary>
    private void CheckTrackedLabelsDisappearance(List<DetectedObject> currentDetections)
    {
        if (_trackedObjects.Count == 0) return;

        var labelsToRemove = new List<string>();
        var currentLabels = currentDetections.Select(d => d.Label).ToHashSet();

        foreach (var (label, trackedDetection) in _trackedObjects)
        {
            // Check if this label is still present
            var matchingDetection = currentDetections.FirstOrDefault(d => d.Label == label);

            if (matchingDetection == null)
            {
                // Label not found in current frame
                _framesSinceLastSeen[label]++;

                if (_framesSinceLastSeen[label] >= DefaultDisappearanceFrameThreshold)
                {
                    Debug.WriteLine($"Label disappeared after {_framesSinceLastSeen[label]} frames: {label}");
                    labelsToRemove.Add(label);

                    LabelDisappeared?.Invoke(this, new LabelDisappearedEventArgs
                    {
                        Label = label,
                        LastDetection = trackedDetection,
                        FramesMissing = _framesSinceLastSeen[label]
                    });
                }
            }
            else
            {
                // Label found - check if it's the same object (similar position) or a different one
                bool isSameObject = IsOverlapping(trackedDetection, matchingDetection, 0.3f);

                if (isSameObject)
                {
                    // Same object, reset counter
                    _framesSinceLastSeen[label] = 0;
                    _trackedObjects[label] = matchingDetection; // Update with latest position
                }
                else
                {
                    // Different object with same label - user moved to a new item
                    Debug.WriteLine($"Label moved to different object: {label}");
                    labelsToRemove.Add(label);

                    LabelDisappeared?.Invoke(this, new LabelDisappearedEventArgs
                    {
                        Label = label,
                        LastDetection = trackedDetection,
                        FramesMissing = 0,
                        MovedToNewObject = true
                    });
                }
            }
        }

        // Remove labels that have disappeared
        foreach (var label in labelsToRemove)
        {
            _trackedObjects.Remove(label);
            _framesSinceLastSeen.Remove(label);
        }
    }

    /// <summary>
    /// Checks if two detections overlap significantly (same object).
    /// </summary>
    private static bool IsOverlapping(DetectedObject a, DetectedObject b, float minIoU)
    {
        int x1 = Math.Max(a.X1, b.X1);
        int y1 = Math.Max(a.Y1, b.Y1);
        int x2 = Math.Min(a.X2, b.X2);
        int y2 = Math.Min(a.Y2, b.Y2);

        int intersectionWidth = Math.Max(0, x2 - x1);
        int intersectionHeight = Math.Max(0, y2 - y1);
        float intersection = intersectionWidth * intersectionHeight;

        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        float union = areaA + areaB - intersection;

        float iou = union > 0 ? intersection / union : 0;
        return iou >= minIoU;
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
    public List<DetectedObject> TertiaryDetections { get; init; } = [];
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

/// <summary>
/// Event arguments for when a tracked label disappears from detection.
/// </summary>
public class LabelDisappearedEventArgs : EventArgs
{
    /// <summary>
    /// The label that disappeared.
    /// </summary>
    public string Label { get; init; } = "";

    /// <summary>
    /// The last detection of this label before it disappeared.
    /// </summary>
    public DetectedObject? LastDetection { get; init; }

    /// <summary>
    /// Number of consecutive frames the label was missing.
    /// </summary>
    public int FramesMissing { get; init; }

    /// <summary>
    /// True if the label moved to a different object (same label, different position).
    /// </summary>
    public bool MovedToNewObject { get; init; }
}

/// <summary>
/// Tracks a detection's position for change detection.
/// Used to determine if same-labeled objects have moved significantly.
/// </summary>
internal readonly struct TrackedPosition
{
    public string Label { get; init; }
    public float CenterX { get; init; }
    public float CenterY { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }

    /// <summary>
    /// Checks if position has changed more than threshold percentage in X or Y.
    /// </summary>
    /// <param name="other">Previous position to compare against.</param>
    /// <param name="threshold">Change threshold as fraction (0.10 = 10%).</param>
    /// <returns>True if position changed significantly.</returns>
    public bool HasMovedSignificantly(TrackedPosition other, float threshold)
    {
        if (Label != other.Label) return true;
        if (FrameWidth == 0 || FrameHeight == 0) return false;

        // Normalize positions to 0-1 range
        float thisNormX = CenterX / FrameWidth;
        float thisNormY = CenterY / FrameHeight;
        float otherNormX = other.CenterX / other.FrameWidth;
        float otherNormY = other.CenterY / other.FrameHeight;

        // Check if either X or Y changed by more than threshold
        return Math.Abs(thisNormX - otherNormX) > threshold ||
               Math.Abs(thisNormY - otherNormY) > threshold;
    }
}
