using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingVision.Models;
using GamingVision.Services.Detection;
using GamingVision.Services.Hotkeys;
using GamingVision.Services.Ocr;
using GamingVision.Services.ScreenCapture;
using GamingVision.Services.Tts;
using GamingVision.Utilities;

namespace GamingVision.ViewModels;

/// <summary>
/// ViewModel for the main window.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigManager _configManager;
    private AppConfiguration _appConfig;
    private ScreenCaptureManager? _captureManager;
    private DetectionManager? _detectionManager;
    private IOcrService? _ocrService;
    private ITtsService? _ttsService;
    private IHotkeyService? _hotkeyService;
    private CapturedFrame? _latestFrame;
    private readonly object _frameLock = new();
    private int _frameCount;
    private int _detectionCount;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<GameProfileItem> _games = [];

    [ObservableProperty]
    private GameProfileItem? _selectedGame;

    [ObservableProperty]
    private bool _isDetectionRunning;

    [ObservableProperty]
    private string _detectionStatus = "Stopped";

    [ObservableProperty]
    private string _lastReadText = "(none)";

    [ObservableProperty]
    private string _gpuInfo = "Detecting...";

    [ObservableProperty]
    private string _modelStatus = "Not loaded";

    [ObservableProperty]
    private int _currentDetectionCount;

    [ObservableProperty]
    private string _hotkeyReadPrimary = "Alt+1";

    [ObservableProperty]
    private string _hotkeyReadSecondary = "Alt+2";

    [ObservableProperty]
    private string _hotkeyReadTertiary = "Alt+3";

    [ObservableProperty]
    private string _hotkeyStopReading = "Alt+4";

    [ObservableProperty]
    private string _hotkeyToggleDetection = "Alt+5";

    [ObservableProperty]
    private string _hotkeyQuit = "Alt+Q";

    public MainViewModel()
    {
        _configManager = new ConfigManager();
        _appConfig = AppConfiguration.CreateDefault();
    }

    /// <summary>
    /// Sets the hotkey service for global hotkey handling.
    /// Must be called before InitializeAsync.
    /// </summary>
    public void SetHotkeyService(IHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>
    /// Initializes the ViewModel by loading configuration.
    /// </summary>
    public async Task InitializeAsync()
    {
        Logger.Log("Initializing MainViewModel");
        _appConfig = await _configManager.LoadAppSettingsAsync();
        await _configManager.LoadAllGameProfilesAsync();
        LoadGames();
        UpdateGpuInfo();
        RegisterHotkeys();
        Logger.Log("MainViewModel initialized");
    }

    /// <summary>
    /// Gets the currently selected game profile.
    /// </summary>
    private GameProfile? GetSelectedGameProfile()
    {
        if (string.IsNullOrEmpty(_appConfig.SelectedGame))
            return null;

        return _configManager.GetGameProfile(_appConfig.SelectedGame);
    }

    /// <summary>
    /// Registers global hotkeys based on the current game profile.
    /// </summary>
    private void RegisterHotkeys()
    {
        if (_hotkeyService == null || !_hotkeyService.IsInitialized)
            return;

        var profile = GetSelectedGameProfile();
        if (profile == null)
            return;

        _hotkeyService.UnregisterAll();

        _hotkeyService.RegisterHotkey(HotkeyId.ReadPrimary, profile.Hotkeys.ReadPrimary);
        _hotkeyService.RegisterHotkey(HotkeyId.ReadSecondary, profile.Hotkeys.ReadSecondary);
        _hotkeyService.RegisterHotkey(HotkeyId.ReadTertiary, profile.Hotkeys.ReadTertiary);
        _hotkeyService.RegisterHotkey(HotkeyId.StopReading, profile.Hotkeys.StopReading);
        _hotkeyService.RegisterHotkey(HotkeyId.ToggleDetection, profile.Hotkeys.ToggleDetection);
        _hotkeyService.RegisterHotkey(HotkeyId.Quit, profile.Hotkeys.Quit);

        Logger.Log("Hotkeys registered");
    }

    /// <summary>
    /// Handles hotkey press events.
    /// </summary>
    private async void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        try
        {
            Logger.Log($"Hotkey pressed: {e.HotkeyId}");

            switch (e.HotkeyId)
            {
                case HotkeyId.ReadPrimary:
                    await ReadPrimaryObjectsAsync();
                    break;

                case HotkeyId.ReadSecondary:
                    await ReadSecondaryObjectsAsync();
                    break;

                case HotkeyId.ReadTertiary:
                    await ReadTertiaryObjectsAsync();
                    break;

                case HotkeyId.StopReading:
                    StopReading();
                    break;

                case HotkeyId.ToggleDetection:
                    await ToggleDetectionAsync();
                    break;

                case HotkeyId.Quit:
                    QuitApplication();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnHotkeyPressed error", ex);
        }
    }

    /// <summary>
    /// Reads the highest priority primary object on demand.
    /// </summary>
    private async Task ReadPrimaryObjectsAsync()
    {
        Logger.Log("ReadPrimaryObjectsAsync: Starting");

        if (_detectionManager == null || !IsDetectionRunning)
        {
            Logger.Log("ReadPrimaryObjectsAsync: Detection not running, playing beep");
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (_ttsService == null)
        {
            Logger.Warn("ReadPrimaryObjectsAsync: TTS service is null");
            return;
        }

        var profile = GetSelectedGameProfile();
        var readLabelAloud = profile?.Detection.ReadPrimaryLabelAloud ?? true;
        Logger.Log($"ReadPrimaryObjectsAsync: readLabelAloud = {readLabelAloud}");

        var primaryDetections = _detectionManager.GetCurrentPrimaryDetections();
        Logger.Log($"ReadPrimaryObjectsAsync: Got {primaryDetections.Count} primary detections");

        if (primaryDetections.Count == 0)
        {
            await SpeakWithVoiceAsync("No primary objects detected", SpeechTier.Primary, interrupt: true);
            return;
        }

        // Get highest priority detection (list is already sorted by priority)
        var detection = primaryDetections[0];
        Logger.Log($"ReadPrimaryObjectsAsync: Highest priority detection = {detection.Label} ({detection.Confidence:F2})");

        // Get frame for OCR
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null)
        {
            Logger.Warn("ReadPrimaryObjectsAsync: Frame is null, speaking label only");
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Primary, interrupt: true);
            return;
        }

        if (frame.IsDisposed)
        {
            Logger.Warn("ReadPrimaryObjectsAsync: Frame is disposed, speaking label only");
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Primary, interrupt: true);
            return;
        }

        if (_ocrService == null || !_ocrService.IsReady)
        {
            Logger.Warn($"ReadPrimaryObjectsAsync: OCR not ready (null: {_ocrService == null}), speaking label only");
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Primary, interrupt: true);
            return;
        }

        // Extract text from the highest priority detection region
        Logger.Log($"ReadPrimaryObjectsAsync: Running OCR on region ({detection.X1},{detection.Y1})-({detection.X2},{detection.Y2})");
        var regions = new List<OcrRegion> { OcrRegion.FromDetection(detection) };
        var textResults = await _ocrService.ExtractTextFromRegionsAsync(
            frame.Data, frame.Width, frame.Height, frame.Stride, regions);

        Logger.Log($"ReadPrimaryObjectsAsync: OCR returned {textResults.Count} results");
        foreach (var kvp in textResults)
        {
            Logger.Log($"ReadPrimaryObjectsAsync: OCR result - '{kvp.Key}': '{kvp.Value}'");
        }

        string displayText;
        string speechText;

        if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            displayText = $"{detection.Label}: {text}";
            speechText = readLabelAloud ? $"{detection.Label}, {text}" : text;
            Logger.Log($"ReadPrimaryObjectsAsync: OCR found text, speechText = '{speechText}'");

            Logger.Log($"ReadPrimaryObjectsAsync: Speaking '{speechText}'");
            await SpeakWithVoiceAsync(speechText, SpeechTier.Primary, interrupt: true);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LastReadText = displayText;
            });
        }
        else
        {
            // OCR found no text - play beep to indicate detection found but no readable text
            Logger.Log($"ReadPrimaryObjectsAsync: No OCR text for '{detection.Label}', playing beep");
            System.Media.SystemSounds.Beep.Play();
        }
    }

    /// <summary>
    /// Reads the highest priority secondary object on demand.
    /// </summary>
    private async Task ReadSecondaryObjectsAsync()
    {
        if (_detectionManager == null || !IsDetectionRunning)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (_ttsService == null)
            return;

        var profile = GetSelectedGameProfile();
        var readLabelAloud = profile?.Detection.ReadSecondaryLabelAloud ?? false;

        var secondaryDetections = _detectionManager.GetCurrentSecondaryDetections();
        if (secondaryDetections.Count == 0)
        {
            await SpeakWithVoiceAsync("No secondary objects detected", SpeechTier.Secondary, interrupt: true);
            return;
        }

        // Get highest priority detection (list is already sorted by priority)
        var detection = secondaryDetections[0];

        // Get frame for OCR
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null || frame.IsDisposed || _ocrService == null || !_ocrService.IsReady)
        {
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Secondary, interrupt: true);
            return;
        }

        // Extract text from the highest priority detection region
        var regions = new List<OcrRegion> { OcrRegion.FromDetection(detection) };
        var textResults = await _ocrService.ExtractTextFromRegionsAsync(
            frame.Data, frame.Width, frame.Height, frame.Stride, regions);

        string displayText;
        string speechText;

        if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            displayText = $"{detection.Label}: {text}";
            speechText = readLabelAloud ? $"{detection.Label}, {text}" : text;

            await SpeakWithVoiceAsync(speechText, SpeechTier.Secondary, interrupt: true);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LastReadText = displayText;
            });
        }
        else
        {
            // OCR found no text - play beep to indicate detection found but no readable text
            System.Media.SystemSounds.Beep.Play();
        }
    }

    /// <summary>
    /// Reads the highest priority tertiary object on demand.
    /// </summary>
    private async Task ReadTertiaryObjectsAsync()
    {
        if (_detectionManager == null || !IsDetectionRunning)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (_ttsService == null)
            return;

        var profile = GetSelectedGameProfile();
        var readLabelAloud = profile?.Detection.ReadTertiaryLabelAloud ?? false;

        var tertiaryDetections = _detectionManager.GetCurrentTertiaryDetections();
        if (tertiaryDetections.Count == 0)
        {
            await SpeakWithVoiceAsync("No tertiary objects detected", SpeechTier.Tertiary, interrupt: true);
            return;
        }

        // Get highest priority detection (list is already sorted by priority)
        var detection = tertiaryDetections[0];

        // Get frame for OCR
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null || frame.IsDisposed || _ocrService == null || !_ocrService.IsReady)
        {
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Tertiary, interrupt: true);
            return;
        }

        // Extract text from the highest priority detection region
        var regions = new List<OcrRegion> { OcrRegion.FromDetection(detection) };
        var textResults = await _ocrService.ExtractTextFromRegionsAsync(
            frame.Data, frame.Width, frame.Height, frame.Stride, regions);

        string displayText;
        string speechText;

        if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            displayText = $"{detection.Label}: {text}";
            speechText = readLabelAloud ? $"{detection.Label}, {text}" : text;

            await SpeakWithVoiceAsync(speechText, SpeechTier.Tertiary, interrupt: true);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LastReadText = displayText;
            });
        }
        else
        {
            // OCR found no text - play beep to indicate detection found but no readable text
            System.Media.SystemSounds.Beep.Play();
        }
    }

    /// <summary>
    /// Sets the TTS voice and rate for the specified tier, then speaks.
    /// </summary>
    private async Task SpeakWithVoiceAsync(string text, SpeechTier tier, bool interrupt = false)
    {
        try
        {
            if (_ttsService == null || !_ttsService.IsReady || string.IsNullOrEmpty(text))
                return;

            var profile = GetSelectedGameProfile();
            if (profile == null)
                return;

            // Set voice and rate based on tier
            switch (tier)
            {
                case SpeechTier.Primary:
                    _ttsService.SetVoice(profile.Tts.PrimaryVoice ?? "");
                    _ttsService.SetRate(profile.Tts.PrimaryRate);
                    break;
                case SpeechTier.Secondary:
                    _ttsService.SetVoice(profile.Tts.SecondaryVoice ?? "");
                    _ttsService.SetRate(profile.Tts.SecondaryRate);
                    break;
                case SpeechTier.Tertiary:
                    _ttsService.SetVoice(profile.Tts.TertiaryVoice ?? "");
                    _ttsService.SetRate(profile.Tts.TertiaryRate);
                    break;
            }

            await _ttsService.SpeakAsync(text, interrupt);
        }
        catch (Exception ex)
        {
            Logger.Error("SpeakWithVoiceAsync error", ex);
        }
    }

    /// <summary>
    /// Stops any current TTS speech.
    /// </summary>
    private void StopReading()
    {
        _ttsService?.Stop();
        _ttsService?.ClearQueue();
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    private void QuitApplication()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current?.Shutdown();
        });
    }

    private void LoadGames()
    {
        Games.Clear();

        foreach (var (gameId, profile) in _configManager.GameProfiles)
        {
            Games.Add(new GameProfileItem
            {
                Key = gameId,
                DisplayName = profile.DisplayName
            });
        }

        // Select the previously selected game
        SelectedGame = Games.FirstOrDefault(g => g.Key == _appConfig.SelectedGame)
                       ?? Games.FirstOrDefault();
    }

    partial void OnSelectedGameChanged(GameProfileItem? value)
    {
        if (value == null) return;

        _appConfig.SelectedGame = value.Key;
        UpdateHotkeyDisplay();
        RegisterHotkeys(); // Re-register hotkeys for the new game profile

        // Save the selection
        Task.Run(async () => await _configManager.SaveAppSettingsAsync(_appConfig));
    }

    private void UpdateHotkeyDisplay()
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        HotkeyReadPrimary = profile.Hotkeys.ReadPrimary;
        HotkeyReadSecondary = profile.Hotkeys.ReadSecondary;
        HotkeyReadTertiary = profile.Hotkeys.ReadTertiary;
        HotkeyStopReading = profile.Hotkeys.StopReading;
        HotkeyToggleDetection = profile.Hotkeys.ToggleDetection;
        HotkeyQuit = profile.Hotkeys.Quit;
    }

    private void UpdateGpuInfo()
    {
        // Run on a thread with proper COM initialization for WMI
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var gpuString = GpuDetector.GetPrimaryGpuDisplayString();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    GpuInfo = gpuString;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPU detection failed: {ex.Message}");
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    GpuInfo = "GPU detection failed (DirectML may still work)";
                });
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
    }

    [RelayCommand]
    private async Task ToggleDetectionAsync()
    {
        if (IsDetectionRunning)
        {
            StopDetection();
        }
        else
        {
            await StartDetectionAsync();
        }
    }

    private async Task StartDetectionAsync()
    {
        Logger.Log("Starting detection");
        var profile = GetSelectedGameProfile();
        if (profile == null)
        {
            Logger.Warn("No game profile selected");
            DetectionStatus = "No game selected";
            return;
        }

        // Initialize detection manager
        _detectionManager?.Dispose();
        _detectionManager = new DetectionManager();
        _detectionManager.DetectionsReady += OnDetectionsReady;
        _detectionManager.PrimaryObjectChanged += OnPrimaryObjectChanged;
        _detectionManager.LabelDisappeared += OnLabelDisappeared;

        DetectionStatus = "Loading model...";
        ModelStatus = "Loading...";

        // Get model path from GameModels directory
        var modelPath = _configManager.GetModelPath(profile.GameId, profile.ModelFile);

        if (!await _detectionManager.InitializeAsync(profile, Path.GetDirectoryName(modelPath)!))
        {
            if (!File.Exists(modelPath))
            {
                DetectionStatus = $"Model not found: {profile.ModelFile}";
                ModelStatus = "Not found";
            }
            else
            {
                DetectionStatus = "Failed to load model";
                ModelStatus = "Load failed";
            }
            return;
        }

        ModelStatus = $"Loaded ({_detectionManager.DetectionService.ExecutionProvider})";

        // Initialize OCR service
        _ocrService?.Dispose();
        _ocrService = new WindowsOcrService();
        if (!await _ocrService.InitializeAsync())
        {
            System.Diagnostics.Debug.WriteLine("Warning: OCR service failed to initialize");
        }

        // Initialize TTS service
        _ttsService?.Dispose();
        _ttsService = new WindowsTtsService();
        if (await _ttsService.InitializeAsync())
        {
            // Apply TTS settings from profile
            _ttsService.SetRate(profile.Tts.PrimaryRate);
            _ttsService.SetVolume(profile.Tts.Volume);

            if (!string.IsNullOrEmpty(profile.Tts.PrimaryVoice))
            {
                _ttsService.SetVoice(profile.Tts.PrimaryVoice);
            }

            System.Diagnostics.Debug.WriteLine("TTS service initialized");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("Warning: TTS service failed to initialize");
        }

        // Initialize capture manager
        _captureManager?.Dispose();
        _captureManager = new ScreenCaptureManager();
        _captureManager.FrameCaptured += OnFrameCaptured;

        DetectionStatus = "Finding window...";

        if (!_captureManager.Initialize(profile))
        {
            DetectionStatus = $"Window not found: {profile.WindowTitle}";
            return;
        }

        if (await _captureManager.StartAsync())
        {
            IsDetectionRunning = true;
            DetectionStatus = "Running";
            _frameCount = 0;
            _detectionCount = 0;
            CurrentDetectionCount = 0;
        }
        else
        {
            DetectionStatus = "Failed to start capture";
        }
    }

    private void StopDetection()
    {
        Logger.Log("Stopping detection");
        _captureManager?.Stop();
        _ttsService?.Stop();
        _ttsService?.ClearQueue();

        if (_detectionManager != null)
        {
            _detectionManager.DetectionsReady -= OnDetectionsReady;
            _detectionManager.PrimaryObjectChanged -= OnPrimaryObjectChanged;
            _detectionManager.LabelDisappeared -= OnLabelDisappeared;
            _detectionManager.StopTrackingAllLabels();
        }

        IsDetectionRunning = false;
        DetectionStatus = "Stopped";
        CurrentDetectionCount = 0;
    }

    private async void OnFrameCaptured(object? sender, CapturedFrame e)
    {
        try
        {
            _frameCount++;

            // Store frame dimensions before they might become invalid
            var frameWidth = e.Width;
            var frameHeight = e.Height;

            // Log every 30 frames to avoid spam
            if (_frameCount % 30 == 1)
            {
                Logger.Log($"OnFrameCaptured: Frame {_frameCount}, {frameWidth}x{frameHeight}, disposed={e.IsDisposed}");
            }

            // Store latest frame for OCR processing
            lock (_frameLock)
            {
                _latestFrame = e;
            }

            // Run detection on this frame
            if (_detectionManager != null && _detectionManager.DetectionService.IsReady)
            {
                try
                {
                    var detections = await _detectionManager.ProcessFrameAsync(e);
                    _detectionCount = detections.Count;
                }
                catch (Exception ex)
                {
                    Logger.Error("Detection error in frame processing", ex);
                }
            }
            else if (_frameCount % 30 == 1)
            {
                Logger.Warn($"OnFrameCaptured: Detection not ready (manager null: {_detectionManager == null}, service ready: {_detectionManager?.DetectionService?.IsReady})");
            }

            // Update UI every 10 frames to avoid excessive updates
            if (_frameCount % 10 == 0)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    DetectionStatus = $"Running (Frame {_frameCount}, {frameWidth}x{frameHeight})";
                    CurrentDetectionCount = _detectionCount;
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnFrameCaptured error", ex);
        }
    }

    private void OnDetectionsReady(object? sender, DetectionEventArgs e)
    {
        // Update detection count on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            CurrentDetectionCount = e.AllDetections.Count;
        });
    }

    private void OnLabelDisappeared(object? sender, LabelDisappearedEventArgs e)
    {
        // Label disappeared or moved to different object - cancel any ongoing TTS
        Logger.Log($"Label disappeared: {e.Label} (MovedToNew: {e.MovedToNewObject}, FramesMissing: {e.FramesMissing})");

        // Cancel current speech and clear queue
        _ttsService?.Stop();
        _ttsService?.ClearQueue();

        // Reset the auto-read cooldown so a new object can be read immediately
        _detectionManager?.ResetCooldown();
    }

    private async void OnPrimaryObjectChanged(object? sender, PrimaryObjectChangedEventArgs e)
    {
        // Primary objects changed - queue for auto-read
        if (e.Detections.Count == 0)
            return;

        // Get detection outside try block so it's in scope for error handling
        var detection = e.Detections[0];

        try
        {
            // Check settings
            var profile = GetSelectedGameProfile();
            var autoReadEnabled = profile?.Detection.AutoReadEnabled ?? true;
            var readLabelAloud = profile?.Detection.ReadPrimaryLabelAloud ?? true;

            Logger.Log($"Primary object changed: {detection.Label} (AutoRead: {autoReadEnabled})");

            // Get current frame for OCR
            CapturedFrame? frame;
            lock (_frameLock)
            {
                frame = _latestFrame;
            }

            if (frame == null || frame.IsDisposed || _ocrService == null || !_ocrService.IsReady)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    LastReadText = detection.Label;
                });
                return;
            }

            // Extract text from the highest priority detection region
            var regions = new List<OcrRegion> { OcrRegion.FromDetection(detection) };
            var textResults = await _ocrService.ExtractTextFromRegionsAsync(
                frame.Data, frame.Width, frame.Height, frame.Stride, regions);

            // Build text result - only speak if OCR found text
            if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                var displayText = $"{detection.Label}: {text}";
                var speechText = readLabelAloud ? $"{detection.Label}, {text}" : text;

                Logger.Log($"OCR result: {displayText}, speechText: '{speechText}'");

                // Speak the extracted text (only if auto-read is enabled)
                if (autoReadEnabled && _ttsService != null && _ttsService.IsReady)
                {
                    // Start tracking the label being read so we can cancel if user moves away
                    _detectionManager?.StartTrackingLabel(detection.Label, detection);

                    await SpeakWithVoiceAsync(speechText, SpeechTier.Primary, interrupt: true);

                    // Stop tracking after speech completes (if not already stopped due to disappearance)
                    _detectionManager?.StopTrackingLabel(detection.Label);
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    LastReadText = displayText;
                });
            }
            else
            {
                // OCR found no text - silently skip for auto-read (no beep for automatic reads)
                Logger.Log($"Auto-read: No OCR text for '{detection.Label}', skipping");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnPrimaryObjectChanged error", ex);

            // Stop tracking on error
            _detectionManager?.StopTrackingAllLabels();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LastReadText = detection.Label;
            });
        }
    }

    [RelayCommand]
    private async Task OpenGameSettingsAsync()
    {
        // Stop detection before changing settings
        var wasRunning = IsDetectionRunning;
        if (wasRunning)
        {
            StopDetection();
        }

        var settingsWindow = new Views.GameSettingsWindow(_appConfig, _configManager);
        settingsWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (settingsWindow.ShowDialog() == true)
        {
            // Reload configs from disk to get saved changes
            _appConfig = await _configManager.LoadAppSettingsAsync();
            await _configManager.LoadAllGameProfilesAsync();

            // Refresh UI
            LoadGames();
            UpdateHotkeyDisplay();

            // Re-register hotkeys with potentially new keybindings
            RegisterHotkeys();

            // Restart detection if it was running before
            if (wasRunning)
            {
                await StartDetectionAsync();
            }
        }
    }

    [RelayCommand]
    private async Task OpenAppSettingsAsync()
    {
        // Stop detection before changing settings
        var wasRunning = IsDetectionRunning;
        if (wasRunning)
        {
            StopDetection();
        }

        var settingsWindow = new Views.AppSettingsWindow(_appConfig, _configManager);
        settingsWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (settingsWindow.ShowDialog() == true)
        {
            // Reload config from disk to get saved changes
            _appConfig = await _configManager.LoadAppSettingsAsync();

            // Restart detection if it was running (will use new settings like DirectML toggle)
            if (wasRunning)
            {
                await StartDetectionAsync();
            }
        }
    }

    /// <summary>
    /// Gets the current app configuration.
    /// </summary>
    public AppConfiguration AppConfig => _appConfig;

    /// <summary>
    /// Gets the config manager for accessing game profiles.
    /// </summary>
    public ConfigManager ConfigManager => _configManager;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from hotkey events
        if (_hotkeyService != null)
        {
            _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        }

        StopDetection();
        _captureManager?.Dispose();
        _detectionManager?.Dispose();
        _ocrService?.Dispose();
        _ttsService?.Dispose();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a game profile item for display in the combo box.
/// </summary>
public class GameProfileItem
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

/// <summary>
/// Specifies which tier of detection is being read aloud.
/// </summary>
public enum SpeechTier
{
    Primary,
    Secondary,
    Tertiary
}
