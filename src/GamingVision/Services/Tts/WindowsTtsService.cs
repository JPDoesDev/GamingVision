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
    private volatile bool _stopRequested;
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

        // Clear stop flag for this new speech request
        _stopRequested = false;

        await Task.Run(() =>
        {
            MemoryStream? memoryStream = null;

            // Render SAPI to memory stream (protected by _speakLock)
            lock (_speakLock)
            {
                try
                {
                    // Check if stop was requested while waiting for lock
                    if (_stopRequested)
                    {
                        Logger.Log("SpeakWithPanAsync: Stop requested, aborting render");
                        return;
                    }

                    // Render speech to WAV in memory
                    memoryStream = new MemoryStream();
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
                }
                catch (Exception ex)
                {
                    Logger.Error("TTS speak with pan error", ex);
                    memoryStream?.Dispose();
                    // Reset synthesizer output in case of error
                    try { _synthesizer.SetOutputToDefaultAudioDevice(); } catch { }
                    return;
                }
            }

            // Play audio with panning (outside _speakLock so Stop() can interrupt quickly)
            // Stream is disposed inside PlayWithPan
            if (memoryStream != null)
            {
                PlayWithPan(memoryStream, pan);
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
        WaveOutEvent? localWaveOut = null;

        try
        {
            // Setup phase: acquire lock, setup player, start playback
            lock (_audioLock)
            {
                // Check if stop was already requested
                if (_stopRequested)
                {
                    Logger.Log("PlayWithPan: Stop requested, aborting playback setup");
                    wavStream.Dispose();
                    return;
                }

                // Dispose previous playback
                if (_waveOut != null)
                {
                    _waveOut.PlaybackStopped -= OnNAudioPlaybackStopped;
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }

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

                localWaveOut = new WaveOutEvent();
                localWaveOut.Init(volumeSample);
                localWaveOut.PlaybackStopped += OnNAudioPlaybackStopped;
                _waveOut = localWaveOut;

                IsSpeaking = true;
                SpeechStarted?.Invoke(this, EventArgs.Empty);

                Logger.Log("PlayWithPan: Starting playback");
                localWaveOut.Play();
            }

            // Wait phase: outside lock so Stop() can interrupt immediately
            // Check both playback state and stop flag
            while (localWaveOut.PlaybackState == PlaybackState.Playing && !_stopRequested)
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
        // Set stop flag first (volatile, no lock needed) so playback loop can exit
        _stopRequested = true;

        // Stop NAudio playback first (quick, allows playback loop to exit)
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

        // Stop SAPI speech (may block briefly if rendering is in progress)
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

    /// <summary>
    /// Plays a beep sound with stereo panning for directional audio feedback.
    /// </summary>
    public Task PlayBeepWithPanAsync(float pan, int frequencyHz = 880, int durationMs = 100)
    {
        // Clamp pan to valid range
        pan = Math.Max(-1f, Math.Min(1f, pan));

        return Task.Run(() =>
        {
            try
            {
                // Generate sine wave samples
                const int sampleRate = 44100;
                int sampleCount = (int)(sampleRate * durationMs / 1000.0);
                var samples = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    // Generate sine wave
                    double time = (double)i / sampleRate;
                    samples[i] = (float)(Math.Sin(2 * Math.PI * frequencyHz * time) * 0.5);

                    // Apply fade in/out to prevent clicks (10ms fade)
                    int fadeSamples = sampleRate / 100; // 10ms
                    if (i < fadeSamples)
                        samples[i] *= (float)i / fadeSamples;
                    else if (i > sampleCount - fadeSamples)
                        samples[i] *= (float)(sampleCount - i) / fadeSamples;
                }

                // Create a sample provider from the buffer
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                var bufferProvider = new BufferSampleProvider(samples, waveFormat);

                // Apply panning
                var panningSample = new PanningSampleProvider(bufferProvider)
                {
                    Pan = pan
                };

                // Apply volume
                var volumeSample = new VolumeSampleProvider(panningSample)
                {
                    Volume = _currentVolume / 100f
                };

                // Play the beep
                using var waveOut = new WaveOutEvent();
                waveOut.Init(volumeSample);
                waveOut.Play();

                // Wait for playback to complete
                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(10);
                }

                Logger.Log($"Sonar beep played (pan={pan:F2}, freq={frequencyHz}Hz, duration={durationMs}ms)");
            }
            catch (Exception ex)
            {
                Logger.Error("Error playing sonar beep", ex);
            }
        });
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

/// <summary>
/// Simple sample provider that reads from a float buffer.
/// Used for playing generated audio like beeps.
/// </summary>
internal class BufferSampleProvider : ISampleProvider
{
    private readonly float[] _buffer;
    private int _position;

    public WaveFormat WaveFormat { get; }

    public BufferSampleProvider(float[] buffer, WaveFormat waveFormat)
    {
        _buffer = buffer;
        WaveFormat = waveFormat;
        _position = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesAvailable = _buffer.Length - _position;
        int samplesToCopy = Math.Min(samplesAvailable, count);

        if (samplesToCopy > 0)
        {
            Array.Copy(_buffer, _position, buffer, offset, samplesToCopy);
            _position += samplesToCopy;
        }

        return samplesToCopy;
    }
}
