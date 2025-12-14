namespace VIGamingVision.Models;

/// <summary>
/// Represents an item in the TTS read queue.
/// </summary>
public class ReadItem
{
    /// <summary>
    /// The text to be read aloud.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Priority level: Primary items are read automatically, Secondary on demand.
    /// </summary>
    public ReadPriority Priority { get; set; } = ReadPriority.Primary;

    /// <summary>
    /// Timestamp when this item was queued.
    /// </summary>
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Priority levels for read items.
/// </summary>
public enum ReadPriority
{
    /// <summary>
    /// Primary objects (titles, item names) - read automatically on change.
    /// </summary>
    Primary,

    /// <summary>
    /// Secondary objects (descriptions, quest text) - read on demand.
    /// </summary>
    Secondary
}
