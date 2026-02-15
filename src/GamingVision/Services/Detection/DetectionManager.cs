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
    private int _lastFrameWidth;
    private int _lastFrameHeight;

    // Reusable lists to avoid per-frame allocations
    private readonly List<DetectedObject> _primaryDetectionsBuffer = new();
    private readonly List<DetectedObject> _secondaryDetectionsBuffer = new();
    private readonly List<DetectedObject> _tertiaryDetectionsBuffer = new();
    private readonly List<DetectedObject> _autoReadPrimaryBuffer = new();

    // Priority lookup dictionary for O(1) label priority lookups
    private Dictionary<string, int>? _priorityLookup;

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
    /// Event raised when auto-read eligible primary detections are found.
    /// Fires every frame that has eligible detections; cooldown is handled by the consumer.
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
    public async Task<bool> InitializeAsync(GameProfile profile, string modelsDirectory)
    {
        _currentProfile = profile;

        // Build priority lookup dictionary for O(1) lookups during sorting
        _priorityLookup = new Dictionary<string, int>(profile.LabelPriority.Count);
        for (int i = 0; i < profile.LabelPriority.Count; i++)
        {
            _priorityLookup[profile.LabelPriority[i]] = i;
        }

        var modelPath = Path.Combine(modelsDirectory, profile.ModelFile);

        if (!File.Exists(modelPath))
        {
            Debug.WriteLine($"Model file not found: {modelPath}");
            return false;
        }

        return await _detectionService.InitializeAsync(modelPath, useGpu: true);
    }

    /// <summary>
    /// Processes a captured frame and runs detection.
    /// </summary>
    public async Task<List<DetectedObject>> ProcessFrameAsync(CapturedFrame frame)
    {
        if (!_detectionService.IsReady || _currentProfile == null)
        {
            Logger.Warn($"ProcessFrameAsync: Early exit - service ready: {_detectionService.IsReady}, profile null: {_currentProfile == null}");
            return [];
        }

        var threshold = _currentProfile.Detection.ConfidenceThreshold;
        var detections = await _detectionService.DetectAsync(frame, threshold);

        // DetectAsync returns null when inference was skipped (already running)
        if (detections == null)
            return [];

        ProcessDetections(detections, frame.Width, frame.Height);
        return detections;
    }

    /// <summary>
    /// Processes detections: categorizes into tiers, fires events.
    /// </summary>
    private void ProcessDetections(List<DetectedObject> detections, int frameWidth, int frameHeight)
    {
        if (_currentProfile == null) return;

        lock (_detectionLock)
        {
            // Clear reusable buffers
            _primaryDetectionsBuffer.Clear();
            _secondaryDetectionsBuffer.Clear();
            _tertiaryDetectionsBuffer.Clear();
            _autoReadPrimaryBuffer.Clear();

            var autoReadThreshold = _currentProfile.Detection.AutoReadConfidenceThreshold;
            var waypointLabel = _currentProfile.Waypoint?.Enabled == true
                ? _currentProfile.Waypoint.Label
                : null;

            // Single-pass filtering into tiers
            foreach (var d in detections)
            {
                if (_currentProfile.PrimaryLabels.Contains(d.Label))
                {
                    _primaryDetectionsBuffer.Add(d);

                    // Auto-read eligible: primary label, above threshold, not the waypoint label
                    if (d.Confidence >= autoReadThreshold &&
                        (string.IsNullOrEmpty(waypointLabel) || d.Label != waypointLabel))
                    {
                        _autoReadPrimaryBuffer.Add(d);
                    }
                }
                else if (_currentProfile.SecondaryLabels.Contains(d.Label))
                {
                    _secondaryDetectionsBuffer.Add(d);
                }
                else if (_currentProfile.TertiaryLabels.Contains(d.Label))
                {
                    _tertiaryDetectionsBuffer.Add(d);
                }
            }

            // Raise detection event (used by overlay)
            DetectionsReady?.Invoke(this, new DetectionEventArgs
            {
                AllDetections = detections,
                PrimaryDetections = _primaryDetectionsBuffer,
                SecondaryDetections = _secondaryDetectionsBuffer,
                TertiaryDetections = _tertiaryDetectionsBuffer,
                Timestamp = DateTime.UtcNow
            });

            // Fire auto-read event if there are eligible primary detections
            if (_autoReadPrimaryBuffer.Count > 0)
            {
                var sorted = SortByPriority(_autoReadPrimaryBuffer, _currentProfile.LabelPriority);

                PrimaryObjectChanged?.Invoke(this, new PrimaryObjectChangedEventArgs
                {
                    Detections = sorted,
                    Timestamp = DateTime.UtcNow
                });
            }

            _lastFrameWidth = frameWidth;
            _lastFrameHeight = frameHeight;
            _lastDetections = detections;
        }
    }

    /// <summary>
    /// Sorts detections by label priority using pre-built lookup dictionary.
    /// </summary>
    private List<DetectedObject> SortByPriority(List<DetectedObject> detections, List<string> priorityOrder)
    {
        if (detections.Count <= 1)
            return detections;

        var lookup = _priorityLookup;
        if (lookup == null || lookup.Count == 0)
        {
            return detections.OrderBy(d => priorityOrder.IndexOf(d.Label) is var idx && idx >= 0 ? idx : int.MaxValue)
                .ThenByDescending(d => d.Confidence)
                .ToList();
        }

        detections.Sort((a, b) =>
        {
            int priorityA = lookup.TryGetValue(a.Label, out var pa) ? pa : int.MaxValue;
            int priorityB = lookup.TryGetValue(b.Label, out var pb) ? pb : int.MaxValue;

            int priorityCompare = priorityA.CompareTo(priorityB);
            if (priorityCompare != 0)
                return priorityCompare;

            return b.Confidence.CompareTo(a.Confidence);
        });

        return detections;
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
            var filtered = _lastDetections.Where(d => _currentProfile.PrimaryLabels.Contains(d.Label)).ToList();
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
    /// Gets the current detection for a specific waypoint label.
    /// If multiple waypoints are detected, returns the one closest to screen center.
    /// </summary>
    public DetectedObject? GetWaypointDetection(string waypointLabel)
    {
        if (string.IsNullOrEmpty(waypointLabel) || _currentProfile == null)
            return null;

        var autoReadThreshold = _currentProfile.Detection.AutoReadConfidenceThreshold;

        lock (_detectionLock)
        {
            var candidates = _lastDetections
                .Where(d => d.Label == waypointLabel && d.Confidence >= autoReadThreshold)
                .ToList();

            if (candidates.Count == 0)
                return null;

            if (candidates.Count == 1)
                return candidates[0];

            float centerX = _lastFrameWidth / 2f;
            float centerY = _lastFrameHeight / 2f;

            return candidates
                .OrderBy(d => {
                    float dx = d.CenterX - centerX;
                    float dy = d.CenterY - centerY;
                    return dx * dx + dy * dy;
                })
                .First();
        }
    }

    /// <summary>
    /// Gets the current detection for a waypoint label optimized for sonar mode.
    /// If multiple waypoints are detected, returns the one with narrowest width.
    /// </summary>
    public DetectedObject? GetSonarWaypointDetection(string waypointLabel)
    {
        if (string.IsNullOrEmpty(waypointLabel) || _currentProfile == null)
            return null;

        var autoReadThreshold = _currentProfile.Detection.AutoReadConfidenceThreshold;

        lock (_detectionLock)
        {
            var candidates = _lastDetections
                .Where(d => d.Label == waypointLabel && d.Confidence >= autoReadThreshold)
                .ToList();

            if (candidates.Count == 0)
                return null;

            if (candidates.Count == 1)
                return candidates[0];

            return candidates.OrderBy(d => d.Width).First();
        }
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
