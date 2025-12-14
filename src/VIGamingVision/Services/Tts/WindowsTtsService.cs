using System.Collections.Concurrent;
using System.Diagnostics;
using System.Speech.Synthesis;

namespace VIGamingVision.Services.Tts;

/// <summary>
/// TTS service using Windows SAPI (System.Speech).
/// Provides reliable, native speech synthesis.
/// </summary>
public class WindowsTtsService : ITtsService
{
    private SpeechSynthesizer? _synthesizer;
    private readonly ConcurrentQueue<string> _speechQueue = new();
    private readonly object _speakLock = new();
    private bool _isProcessingQueue;
    private bool _disposed;
    private List<VoiceInfo> _availableVoices = [];

    public bool IsReady => _synthesizer != null;
    public bool IsSpeaking { get; private set; }
    public IReadOnlyList<VoiceInfo> AvailableVoices => _availableVoices;
    public VoiceInfo? CurrentVoice { get; private set; }

    public event EventHandler? SpeechStarted;
    public event EventHandler? SpeechCompleted;

    /// <summary>
    /// Initializes the TTS service.
    /// </summary>
    public Task<bool> InitializeAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                _synthesizer = new SpeechSynthesizer();
                _synthesizer.SpeakStarted += OnSpeakStarted;
                _synthesizer.SpeakCompleted += OnSpeakCompleted;

                // Get available voices
                _availableVoices = _synthesizer.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => new VoiceInfo
                    {
                        Name = v.VoiceInfo.Name,
                        Culture = v.VoiceInfo.Culture.Name,
                        Gender = v.VoiceInfo.Gender.ToString(),
                        Description = v.VoiceInfo.Description
                    })
                    .ToList();

                // Set current voice info
                if (_synthesizer.Voice != null)
                {
                    CurrentVoice = _availableVoices
                        .FirstOrDefault(v => v.Name == _synthesizer.Voice.Name);
                }

                Debug.WriteLine($"TTS initialized with {_availableVoices.Count} voices");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TTS initialization error: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Speaks the given text asynchronously.
    /// </summary>
    public async Task SpeakAsync(string text, bool interrupt = false)
    {
        if (_synthesizer == null || string.IsNullOrWhiteSpace(text))
            return;

        if (interrupt)
        {
            Stop();
        }

        await Task.Run(() =>
        {
            lock (_speakLock)
            {
                try
                {
                    _synthesizer.SpeakAsync(text);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TTS speak error: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Queues text to be spoken after current speech completes.
    /// </summary>
    public void QueueSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _speechQueue.Enqueue(text);
        ProcessQueue();
    }

    /// <summary>
    /// Stops any current speech.
    /// </summary>
    public void Stop()
    {
        if (_synthesizer == null)
            return;

        lock (_speakLock)
        {
            try
            {
                _synthesizer.SpeakAsyncCancelAll();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TTS stop error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears the speech queue.
    /// </summary>
    public void ClearQueue()
    {
        while (_speechQueue.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Sets the speech rate.
    /// </summary>
    public void SetRate(int rate)
    {
        if (_synthesizer == null)
            return;

        // Clamp to valid range
        rate = Math.Max(-10, Math.Min(10, rate));
        _synthesizer.Rate = rate;
    }

    /// <summary>
    /// Sets the speech volume.
    /// </summary>
    public void SetVolume(int volume)
    {
        if (_synthesizer == null)
            return;

        // Clamp to valid range
        volume = Math.Max(0, Math.Min(100, volume));
        _synthesizer.Volume = volume;
    }

    /// <summary>
    /// Sets the voice to use.
    /// </summary>
    public bool SetVoice(string voiceName)
    {
        if (_synthesizer == null || string.IsNullOrWhiteSpace(voiceName))
            return false;

        try
        {
            // Try exact match first
            var voice = _availableVoices.FirstOrDefault(v =>
                v.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase));

            if (voice == null)
            {
                // Try partial match
                voice = _availableVoices.FirstOrDefault(v =>
                    v.Name.Contains(voiceName, StringComparison.OrdinalIgnoreCase));
            }

            if (voice != null)
            {
                _synthesizer.SelectVoice(voice.Name);
                CurrentVoice = voice;
                Debug.WriteLine($"TTS voice set to: {voice.Name}");
                return true;
            }

            Debug.WriteLine($"TTS voice not found: {voiceName}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TTS set voice error: {ex.Message}");
            return false;
        }
    }

    private void ProcessQueue()
    {
        if (_isProcessingQueue || _synthesizer == null)
            return;

        lock (_speakLock)
        {
            if (_isProcessingQueue)
                return;

            _isProcessingQueue = true;
        }

        Task.Run(() =>
        {
            try
            {
                while (_speechQueue.TryDequeue(out var text))
                {
                    if (_synthesizer == null || _disposed)
                        break;

                    _synthesizer.Speak(text);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TTS queue processing error: {ex.Message}");
            }
            finally
            {
                _isProcessingQueue = false;
            }
        });
    }

    private void OnSpeakStarted(object? sender, SpeakStartedEventArgs e)
    {
        IsSpeaking = true;
        SpeechStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        IsSpeaking = false;
        SpeechCompleted?.Invoke(this, EventArgs.Empty);

        // Process next item in queue
        if (!_speechQueue.IsEmpty)
        {
            ProcessQueue();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClearQueue();
        Stop();

        if (_synthesizer != null)
        {
            _synthesizer.SpeakStarted -= OnSpeakStarted;
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
            _synthesizer.Dispose();
            _synthesizer = null;
        }

        GC.SuppressFinalize(this);
    }
}
