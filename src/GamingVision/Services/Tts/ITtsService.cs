namespace GamingVision.Services.Tts;

/// <summary>
/// Interface for Text-to-Speech services.
/// </summary>
public interface ITtsService : IDisposable
{
    /// <summary>
    /// Gets whether the TTS service is ready to speak.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Gets whether the TTS service is currently speaking.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Initializes the TTS service.
    /// </summary>
    /// <returns>True if initialization succeeded.</returns>
    Task<bool> InitializeAsync();

    /// <summary>
    /// Speaks the given text asynchronously.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="interrupt">If true, interrupts any current speech.</param>
    Task SpeakAsync(string text, bool interrupt = false);

    /// <summary>
    /// Speaks the given text with stereo panning for directional audio.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="pan">Pan value from -1.0 (full left) to 1.0 (full right), 0.0 is center.</param>
    /// <param name="interrupt">If true, interrupts any current speech.</param>
    Task SpeakWithPanAsync(string text, float pan, bool interrupt = false);

    /// <summary>
    /// Queues text to be spoken after current speech completes.
    /// </summary>
    /// <param name="text">The text to queue.</param>
    void QueueSpeech(string text);

    /// <summary>
    /// Stops any current speech.
    /// </summary>
    void Stop();

    /// <summary>
    /// Clears the speech queue.
    /// </summary>
    void ClearQueue();

    /// <summary>
    /// Sets the speech rate.
    /// </summary>
    /// <param name="rate">Rate from -10 (slowest) to 10 (fastest), 0 is normal.</param>
    void SetRate(int rate);

    /// <summary>
    /// Sets the speech volume.
    /// </summary>
    /// <param name="volume">Volume from 0 (silent) to 100 (loudest).</param>
    void SetVolume(int volume);

    /// <summary>
    /// Sets the voice to use.
    /// </summary>
    /// <param name="voiceName">The name of the voice to use.</param>
    /// <returns>True if the voice was found and set.</returns>
    bool SetVoice(string voiceName);

    /// <summary>
    /// Gets the available voices on this system.
    /// </summary>
    IReadOnlyList<VoiceInfo> AvailableVoices { get; }

    /// <summary>
    /// Gets the currently selected voice.
    /// </summary>
    VoiceInfo? CurrentVoice { get; }

    /// <summary>
    /// Event raised when speech starts.
    /// </summary>
    event EventHandler? SpeechStarted;

    /// <summary>
    /// Event raised when speech completes.
    /// </summary>
    event EventHandler? SpeechCompleted;
}

/// <summary>
/// Information about an available voice.
/// </summary>
public class VoiceInfo
{
    /// <summary>
    /// The name of the voice.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The language/culture of the voice (e.g., "en-US").
    /// </summary>
    public string Culture { get; init; } = string.Empty;

    /// <summary>
    /// The gender of the voice.
    /// </summary>
    public string Gender { get; init; } = string.Empty;

    /// <summary>
    /// Description or additional info about the voice.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    public override string ToString() => $"{Name} ({Culture})";
}
