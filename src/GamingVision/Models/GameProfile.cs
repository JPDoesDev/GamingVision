namespace GamingVision.Models;

/// <summary>
/// Complete configuration profile for a single game.
/// All settings are per-game to allow different configurations for different games.
/// Stored in GameModels/{gameId}/game_config.json.
/// </summary>
public class GameProfile
{
    /// <summary>
    /// Unique identifier for this game (matches folder name in GameModels).
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name shown in the launcher.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Path to the ONNX model file for this game.
    /// </summary>
    public string ModelFile { get; set; } = string.Empty;

    /// <summary>
    /// Window title for capture (partial match supported).
    /// </summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// Object types that trigger auto-read.
    /// </summary>
    public List<string> PrimaryLabels { get; set; } = [];

    /// <summary>
    /// Object types read on hotkey only.
    /// </summary>
    public List<string> SecondaryLabels { get; set; } = [];

    /// <summary>
    /// Third category of object types read on hotkey only.
    /// </summary>
    public List<string> TertiaryLabels { get; set; } = [];

    /// <summary>
    /// Order to process when multiple objects detected.
    /// </summary>
    public List<string> LabelPriority { get; set; } = [];

    /// <summary>
    /// All available labels for this game with their descriptions.
    /// Used for label configuration UI. Must be kept in sync with the model's classes.txt.
    /// </summary>
    public List<LabelDefinition> Labels { get; set; } = [];

    /// <summary>
    /// Hotkey configuration for this game.
    /// </summary>
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>
    /// Screen capture configuration for this game.
    /// </summary>
    public CaptureSettings Capture { get; set; } = new();

    /// <summary>
    /// Text-to-speech configuration for this game.
    /// </summary>
    public TtsSettings Tts { get; set; } = new();

    /// <summary>
    /// Detection parameters for this game.
    /// </summary>
    public DetectionSettings Detection { get; set; } = new();

    /// <summary>
    /// Visual overlay settings for bounding box display.
    /// </summary>
    public OverlaySettings? Overlay { get; set; }

    /// <summary>
    /// Creates a deep copy of this game profile.
    /// </summary>
    public GameProfile Clone() => new()
    {
        GameId = GameId,
        DisplayName = DisplayName,
        ModelFile = ModelFile,
        WindowTitle = WindowTitle,
        PrimaryLabels = [.. PrimaryLabels],
        SecondaryLabels = [.. SecondaryLabels],
        TertiaryLabels = [.. TertiaryLabels],
        LabelPriority = [.. LabelPriority],
        Labels = Labels.Select(l => l.Clone()).ToList(),
        Hotkeys = Hotkeys.Clone(),
        Capture = Capture.Clone(),
        Tts = Tts.Clone(),
        Detection = Detection.Clone(),
        Overlay = Overlay?.Clone()
    };
}
