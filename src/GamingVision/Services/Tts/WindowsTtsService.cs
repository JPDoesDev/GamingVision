using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using GamingVision.Utilities;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GamingVision.Services.Tts;

/// <summary>
/// TTS service using Windows SAPI (System.Speech).
/// Provides reliable, native speech synthesis.
/// </summary>
public class WindowsTtsService : ITtsService
{
    private SpeechSynthesizer? _synthesizer;
    private readonly ConcurrentQueue<string> _speechQueue = new();
    private readonly object _speakLock = new();
    private readonly object _audioLock = new();
    private bool _isProcessingQueue;
    private bool _disposed;
    private List<VoiceInfo> _availableVoices = [];
    private WaveOutEvent? _waveOut;
    private int _currentVolume = 100;

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
        {
            Logger.Warn($"SpeakAsync: Skipped - synthesizer null or empty text");
            return;
        }

        Logger.Log($"SpeakAsync: Speaking '{text.Substring(0, Math.Min(50, text.Length))}' (interrupt: {interrupt})");

        if (interrupt)
        {
            Logger.Log("SpeakAsync: Interrupting current speech");
            Stop();
        }

        await Task.Run(() =>
        {
            lock (_speakLock)
            {
                try
                {
                    Logger.Log("SpeakAsync: Calling synthesizer.SpeakAsync");
                    _synthesizer.SpeakAsync(text);
                    Logger.Log("SpeakAsync: SpeakAsync call completed");
                }
                catch (Exception ex)
                {
                    Logger.Error("TTS speak error", ex);
                }
            }
        });
    }

    /// <summary>
    /// Speaks the given text with stereo panning for directional audio.
    /// Uses NAudio to render SAPI output to a stream, apply panning, and play.
    /// </summary>
    public async Task SpeakWithPanAsync(string text, float pan, bool interrupt = false)
    {
        if (_synthesizer == null || string.IsNullOrWhiteSpace(text))
        {
            Logger.Warn($"SpeakWithPanAsync: Skipped - synthesizer null or empty text");
            return;
        }

        // Clamp pan to valid range
        pan = Math.Clamp(pan, -1.0f, 1.0f);

        Logger.Log($"SpeakWithPanAsync: Speaking '{text.Substring(0, Math.Min(50, text.Length))}' (pan: {pan:F2}, interrupt: {interrupt})");

        if (interrupt)
        {
            Logger.Log("SpeakWithPanAsync: Interrupting current speech");
            Stop();
        }

        await Task.Run(() =>
        {
            lock (_speakLock)
            {
                try
                {
                    // Render speech to WAV in memory
                    var memoryStream = new MemoryStream();
                    _synthesizer.SetOutputToWaveStream(memoryStream);
                    _synthesizer.Speak(text);
                    _synthesizer.SetOutputToDefaultAudioDevice();

                    Logger.Log($"SpeakWithPanAsync: SAPI rendered {memoryStream.Length} bytes to stream");

                    if (memoryStream.Length == 0)
                    {
                        Logger.Warn("SpeakWithPanAsync: SAPI produced no audio data");
                        memoryStream.Dispose();
                        return;
                    }

                    memoryStream.Position = 0;

                    // Play with NAudio and panning (stream is disposed inside PlayWithPan)
                    PlayWithPan(memoryStream, pan);
                }
                catch (Exception ex)
                {
                    Logger.Error("TTS speak with pan error", ex);
                    // Reset synthesizer output in case of error
                    try { _synthesizer.SetOutputToDefaultAudioDevice(); } catch { }
                }
            }
        });
    }

    /// <summary>
    /// Plays WAV audio from a stream with stereo panning applied.
    /// Takes ownership of the stream and disposes it when done.
    /// </summary>
    private void PlayWithPan(MemoryStream wavStream, float pan)
    {
        WaveFileReader? reader = null;

        lock (_audioLock)
        {
            // Dispose previous playback
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnNAudioPlaybackStopped;
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            try
            {
                reader = new WaveFileReader(wavStream);
                Logger.Log($"PlayWithPan: WAV format - {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ch, {reader.WaveFormat.BitsPerSample}bit");

                var sampleProvider = reader.ToSampleProvider();

                // Build the sample provider chain based on input channels
                ISampleProvider outputProvider;

                if (sampleProvider.WaveFormat.Channels == 1)
                {
                    // Mono input: PanningSampleProvider takes mono and outputs stereo with panning
                    var panningSample = new PanningSampleProvider(sampleProvider)
                    {
                        Pan = pan
                    };
                    outputProvider = panningSample;
                    Logger.Log($"PlayWithPan: Applied panning to mono source (pan={pan:F2})");
                }
                else
                {
                    // Stereo input: Can't use PanningSampleProvider, just pass through
                    // (This shouldn't happen with SAPI which outputs mono)
                    outputProvider = sampleProvider;
                    Logger.Log("PlayWithPan: Stereo source, panning not applied");
                }

                // Apply volume (convert 0-100 to 0.0-1.0)
                var volumeSample = new VolumeSampleProvider(outputProvider)
                {
                    Volume = _currentVolume / 100f
                };

                Logger.Log($"PlayWithPan: Volume set to {_currentVolume / 100f:F2}");

                _waveOut = new WaveOutEvent();
                _waveOut.Init(volumeSample);
                _waveOut.PlaybackStopped += OnNAudioPlaybackStopped;

                IsSpeaking = true;
                SpeechStarted?.Invoke(this, EventArgs.Empty);

                Logger.Log("PlayWithPan: Starting playback");
                _waveOut.Play();

                // Wait for playback to complete (synchronous in this Task.Run context)
                while (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(10);
                }

                Logger.Log("PlayWithPan: Playback complete");
            }
            catch (Exception ex)
            {
                Logger.Error("NAudio playback error", ex);
                IsSpeaking = false;
            }
            finally
            {
                // Clean up reader and stream
                reader?.Dispose();
                wavStream.Dispose();
            }
        }
    }

    /// <summary>
    /// Handles NAudio playback stopped event.
    /// </summary>
    private void OnNAudioPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        IsSpeaking = false;
        SpeechCompleted?.Invoke(this, EventArgs.Empty);
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
    /// Stops any current speech (both SAPI and NAudio).
    /// </summary>
    public void Stop()
    {
        // Stop SAPI speech
        if (_synthesizer != null)
        {
            lock (_speakLock)
            {
                try
                {
                    Logger.Log("TTS Stop: Cancelling all SAPI speech");
                    _synthesizer.SpeakAsyncCancelAll();
                }
                catch (Exception ex)
                {
                    Logger.Error("TTS SAPI stop error", ex);
                }
            }
        }

        // Stop NAudio playback
        lock (_audioLock)
        {
            try
            {
                if (_waveOut != null)
                {
                    Logger.Log("TTS Stop: Stopping NAudio playback");
                    _waveOut.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("TTS NAudio stop error", ex);
            }
        }

        Logger.Log("TTS Stop: Cancelled");
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
        _currentVolume = volume;
        _synthesizer.Volume = volume;
    }

    /// <summary>
    /// Sets the voice to use.
    /// </summary>
    public bool SetVoice(string voiceName)
    {
        if (_synthesizer == null || string.IsNullOrWhiteSpace(voiceName))
        {
            Logger.Warn($"SetVoice: Skipped - synthesizer null or empty voice name");
            return false;
        }

        try
        {
            Logger.Log($"SetVoice: Setting voice to '{voiceName}'");

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
                Logger.Log($"SetVoice: Calling SelectVoice({voice.Name})");
                _synthesizer.SelectVoice(voice.Name);
                CurrentVoice = voice;
                Logger.Log($"SetVoice: Voice set to: {voice.Name}");
                return true;
            }

            Logger.Warn($"SetVoice: Voice not found: {voiceName}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("SetVoice error", ex);
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

        // Dispose NAudio resources
        lock (_audioLock)
        {
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnNAudioPlaybackStopped;
                _waveOut.Dispose();
                _waveOut = null;
            }
        }

        // Dispose SAPI resources
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
