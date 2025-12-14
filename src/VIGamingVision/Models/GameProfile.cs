namespace VIGamingVision.Models;

/// <summary>
/// Complete configuration profile for a single game.
/// All settings are per-game to allow different configurations for different games.
/// </summary>
public class GameProfile
{
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
    /// Order to process when multiple objects detected.
    /// </summary>
    public List<string> LabelPriority { get; set; } = [];

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
    /// Creates a deep copy of this game profile.
    /// </summary>
    public GameProfile Clone() => new()
    {
        DisplayName = DisplayName,
        ModelFile = ModelFile,
        WindowTitle = WindowTitle,
        PrimaryLabels = [.. PrimaryLabels],
        SecondaryLabels = [.. SecondaryLabels],
        LabelPriority = [.. LabelPriority],
        Hotkeys = Hotkeys.Clone(),
        Capture = Capture.Clone(),
        Tts = Tts.Clone(),
        Detection = Detection.Clone()
    };
}
