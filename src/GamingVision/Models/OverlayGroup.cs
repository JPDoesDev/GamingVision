namespace GamingVision.Models;

/// <summary>
/// Defines a group of labels that share visual styling in the overlay.
/// </summary>
public class OverlayGroup
{
    /// <summary>
    /// User-defined name for this group (e.g., "Enemies", "Items").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Labels included in this group (must match model labels).
    /// </summary>
    public List<string> Labels { get; set; } = [];

    /// <summary>
    /// Outline/fill color in hex format (#RRGGBB or #AARRGGBB).
    /// </summary>
    public string Color { get; set; } = "#FF0000";

    /// <summary>
    /// Line thickness in pixels (1-10).
    /// </summary>
    public int Thickness { get; set; } = 2;

    /// <summary>
    /// Whether to display the label name above the box.
    /// </summary>
    public bool ShowLabel { get; set; } = true;

    /// <summary>
    /// Visual style for the bounding box.
    /// Values: "solid", "dashed", "filled", "highContrast", "highContrastInverted"
    /// </summary>
    public string Style { get; set; } = "solid";

    /// <summary>
    /// Minimum confidence threshold for this group (0.0-1.0).
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.1f;

    /// <summary>
    /// Creates a deep copy of this group.
    /// </summary>
    public OverlayGroup Clone() => new()
    {
        Name = Name,
        Labels = [.. Labels],
        Color = Color,
        Thickness = Thickness,
        ShowLabel = ShowLabel,
        Style = Style,
        ConfidenceThreshold = ConfidenceThreshold
    };
}
