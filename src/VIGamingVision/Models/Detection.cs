namespace VIGamingVision.Models;

/// <summary>
/// Represents a single object detection result from YOLO inference.
/// </summary>
public class DetectedObject
{
    /// <summary>
    /// The label/class name of the detected object.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Bounding box X coordinate (left).
    /// </summary>
    public int X1 { get; set; }

    /// <summary>
    /// Bounding box Y coordinate (top).
    /// </summary>
    public int Y1 { get; set; }

    /// <summary>
    /// Bounding box X coordinate (right).
    /// </summary>
    public int X2 { get; set; }

    /// <summary>
    /// Bounding box Y coordinate (bottom).
    /// </summary>
    public int Y2 { get; set; }

    /// <summary>
    /// Width of the bounding box.
    /// </summary>
    public int Width => X2 - X1;

    /// <summary>
    /// Height of the bounding box.
    /// </summary>
    public int Height => Y2 - Y1;

    /// <summary>
    /// Center X coordinate of the bounding box.
    /// </summary>
    public float CenterX => (X1 + X2) / 2f;

    /// <summary>
    /// Center Y coordinate of the bounding box.
    /// </summary>
    public float CenterY => (Y1 + Y2) / 2f;

    /// <summary>
    /// Calculates the distance from this detection's center to a point.
    /// </summary>
    public float DistanceTo(float x, float y)
    {
        var dx = CenterX - x;
        var dy = CenterY - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
