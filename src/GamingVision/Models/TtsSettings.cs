namespace GamingVision.Models;

/// <summary>
/// Text-to-speech configuration for a game profile.
/// </summary>
public class TtsSettings
{
    /// <summary>
    /// TTS engine: "sapi" (Windows voices) or "piper" (neural voices).
    /// </summary>
    public string Engine { get; set; } = "sapi";

    /// <summary>
    /// Voice name for primary objects.
    /// </summary>
    public string PrimaryVoice { get; set; } = "Microsoft David";

    /// <summary>
    /// Speech rate for primary objects (-10 to 10 for SAPI).
    /// </summary>
    public int PrimaryRate { get; set; } = 3;

    /// <summary>
    /// Voice name for secondary objects.
    /// </summary>
    public string SecondaryVoice { get; set; } = "Microsoft David";

    /// <summary>
    /// Speech rate for secondary objects (-10 to 10 for SAPI).
    /// </summary>
    public int SecondaryRate { get; set; } = 0;

    /// <summary>
    /// Volume level (0-100).
    /// </summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    /// Creates a deep copy of this instance.
    /// </summary>
    public TtsSettings Clone() => new()
    {
        Engine = Engine,
        PrimaryVoice = PrimaryVoice,
        PrimaryRate = PrimaryRate,
        SecondaryVoice = SecondaryVoice,
        SecondaryRate = SecondaryRate,
        Volume = Volume
    };
}
