namespace GamingVision.Models;

/// <summary>
/// Represents a box style option for the overlay group dropdown.
/// </summary>
public class StyleOption
{
    public string Value { get; }
    public string DisplayName { get; }

    public StyleOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
