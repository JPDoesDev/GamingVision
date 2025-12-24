namespace GamingVision.Models;

/// <summary>
/// Defines a detection label with its name and description.
/// Labels are stored in game_config.json and used for configuration UI.
/// </summary>
public class LabelDefinition
{
    /// <summary>
    /// The label name as used by the YOLO model (must match classes.txt).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what this label detects.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Creates a deep copy of this label definition.
    /// </summary>
    public LabelDefinition Clone() => new()
    {
        Name = Name,
        Description = Description
    };
}