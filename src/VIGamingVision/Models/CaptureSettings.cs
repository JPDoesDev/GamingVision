namespace VIGamingVision.Models;

/// <summary>
/// Screen capture configuration for a game profile.
/// </summary>
public class CaptureSettings
{
    /// <summary>
    /// Capture method: "window" or "fullscreen".
    /// </summary>
    public string Method { get; set; } = "window";

    /// <summary>
    /// Monitor index for fullscreen capture (0 = primary).
    /// </summary>
    public int MonitorIndex { get; set; } = 0;

    /// <summary>
    /// Include the cursor in captures.
    /// </summary>
    public bool CaptureCursor { get; set; } = false;

    /// <summary>
    /// Scale factor for capture (1.0 = original size, 0.5 = half size).
    /// Lower values improve performance but may reduce detection accuracy.
    /// </summary>
    public float ScaleFactor { get; set; } = 1.0f;

    /// <summary>
    /// Region of interest - only capture this area (null = full window).
    /// Format: "x,y,width,height" or empty for full capture.
    /// </summary>
    public string RegionOfInterest { get; set; } = "";

    /// <summary>
    /// Creates a deep copy of this instance.
    /// </summary>
    public CaptureSettings Clone() => new()
    {
        Method = Method,
        MonitorIndex = MonitorIndex,
        CaptureCursor = CaptureCursor,
        ScaleFactor = ScaleFactor,
        RegionOfInterest = RegionOfInterest
    };
}
