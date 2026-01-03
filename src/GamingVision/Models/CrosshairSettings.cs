namespace GamingVision.Models;

/// <summary>
/// Configuration for the crosshair overlay feature.
/// Stored in game_config.json alongside other game settings.
/// </summary>
public class CrosshairSettings
{
    /// <summary>
    /// Horizontal offset from screen center in pixels (-100 to +100).
    /// </summary>
    public int OffsetX { get; set; } = 0;

    /// <summary>
    /// Vertical offset from screen center in pixels (-100 to +100).
    /// </summary>
    public int OffsetY { get; set; } = 0;

    /// <summary>
    /// Shape of the crosshair: "Cross", "Circle", or "Box".
    /// </summary>
    public string Shape { get; set; } = "Circle";

    /// <summary>
    /// Color of the crosshair in hex format (e.g., "#FF0000").
    /// </summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>
    /// Color of the crosshair outline in hex format (e.g., "#000000").
    /// </summary>
    public string OutlineColor { get; set; } = "#000000";

    /// <summary>
    /// Size/radius of the crosshair in pixels (1-100).
    /// </summary>
    public int Size { get; set; } = 20;

    /// <summary>
    /// Thickness of the crosshair lines in pixels (1-25).
    /// </summary>
    public int Thickness { get; set; } = 2;

    /// <summary>
    /// Thickness of the crosshair outline in pixels (1-25).
    /// </summary>
    public int OutlineThickness { get; set; } = 1;

    /// <summary>
    /// Creates a deep copy of these settings.
    /// </summary>
    public CrosshairSettings Clone() => new()
    {
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Shape = Shape,
        Color = Color,
        OutlineColor = OutlineColor,
        Size = Size,
        Thickness = Thickness,
        OutlineThickness = OutlineThickness
    };
}
