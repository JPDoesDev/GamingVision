using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingVision.Models;
using GamingVision.Rendering;
using GamingVision.Services.Detection;
using GamingVision.Services.Hotkeys;
using GamingVision.Services.Ocr;
using GamingVision.Services.ScreenCapture;
using GamingVision.Services.Tts;
using GamingVision.Services.Training;
using GamingVision.Utilities;
using GamingVision.Views;
using GamingVision.Windows;

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

    // Overlay fields
    private OverlayWindow? _overlayWindow;
    private OverlayRenderer? _overlayRenderer;
    private OverlayHotkeyService? _overlayHotkeyService;
    private bool _overlayVisible = true;
    private volatile bool _stoppingOverlay;

    // Crosshair fields
    private CrosshairWindow? _crosshairWindow;
    private CrosshairRenderer? _crosshairRenderer;
    private CrosshairHotkeyService? _crosshairHotkeyService;
    private bool _crosshairVisible = true;
    private volatile bool _stoppingCrosshair;

    // Training fields
    private TrainingDataManager? _trainingDataManager;
    private int _trainingCaptureCount;

    // Window polling fields for waiting/recovery
    private CancellationTokenSource? _windowPollingCts;
    private Task? _windowPollingTask;

    // Waypoint timer fields
    private System.Timers.Timer? _waypointTimer;
    private WaypointSettings? _waypointSettings;
    private volatile bool _sonarArmed; // For sonar mode: true when ready to beep on next detection

    [ObservableProperty]
    private ObservableCollection<GameProfileItem> _games = [];

    [ObservableProperty]
    private GameProfileItem? _selectedGame;

    [ObservableProperty]
    private bool _isEngineRunning;

    [ObservableProperty]
    private bool _isWaitingForWindow;

    [ObservableProperty]
    private bool _isScreenReaderEnabled = true;

    [ObservableProperty]
    private bool _isOverlayEnabled;

    [ObservableProperty]
    private bool _isTrainingEnabled = true;

    [ObservableProperty]
    private string _trainingDataPath = string.Empty;

    [ObservableProperty]
    private string _trainingCaptureHotkey = "F1";

    [ObservableProperty]
    private string _trainingStatus = string.Empty;

    [ObservableProperty]
    private float _annotationConfidenceThreshold = 0.1f;

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

    // Overlay properties
    [ObservableProperty]
    private bool _isOverlayRunning;

    [ObservableProperty]
    private string _overlayToggleHotkey = "Alt+O";

    [ObservableProperty]
    private ObservableCollection<OverlayGroup> _overlayGroups = [];

    [ObservableProperty]
    private OverlayGroup? _selectedOverlayGroup;

    // Crosshair properties
    [ObservableProperty]
    private bool _isCrosshairEnabled;

    [ObservableProperty]
    private bool _isCrosshairRunning;

    [ObservableProperty]
    private string _crosshairToggleHotkey = "Alt+C";

    // Post-processing properties
    [ObservableProperty]
    private bool _isPostProcessing;

    [ObservableProperty]
    private string _postProcessStatus = string.Empty;

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

        // Load saved feature toggle states
        IsScreenReaderEnabled = _appConfig.ScreenReaderEnabled;
        IsOverlayEnabled = _appConfig.OverlayEnabled;
        IsCrosshairEnabled = _appConfig.CrosshairEnabled;

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

        // Register training capture hotkey
        var trainingHotkey = profile.Training?.CaptureHotkey ?? "F1";
        _hotkeyService.RegisterHotkey(HotkeyId.CaptureTraining, trainingHotkey);

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
                    await ToggleEngineAsync();
                    break;

                case HotkeyId.Quit:
                    QuitApplication();
                    break;

                case HotkeyId.CaptureTraining:
                    await CaptureTrainingScreenshotAsync();
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

        if (_detectionManager == null || !IsEngineRunning)
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
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Primary, detection, 0, interrupt: true);
            return;
        }

        if (frame.IsDisposed)
        {
            Logger.Warn("ReadPrimaryObjectsAsync: Frame is disposed, speaking label only");
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Primary, detection, 0, interrupt: true);
            return;
        }

        if (_ocrService == null || !_ocrService.IsReady)
        {
            Logger.Warn($"ReadPrimaryObjectsAsync: OCR not ready (null: {_ocrService == null}), speaking label only");
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Primary, detection, frame.Width, interrupt: true);
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
            await SpeakWithVoiceAsync(speechText, SpeechTier.Primary, detection, frame.Width, interrupt: true);

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
        if (_detectionManager == null || !IsEngineRunning)
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
            int screenWidth = frame?.Width ?? 0;
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Secondary, detection, screenWidth, interrupt: true);
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

            await SpeakWithVoiceAsync(speechText, SpeechTier.Secondary, detection, frame.Width, interrupt: true);

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
        if (_detectionManager == null || !IsEngineRunning)
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
            int screenWidth = frame?.Width ?? 0;
            await SpeakWithVoiceAsync(detection.Label, SpeechTier.Tertiary, detection, screenWidth, interrupt: true);
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

            await SpeakWithVoiceAsync(speechText, SpeechTier.Tertiary, detection, frame.Width, interrupt: true);

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
    /// Sets the TTS voice and rate for the specified tier, then speaks with optional directional audio.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="tier">The speech tier (Primary, Secondary, Tertiary).</param>
    /// <param name="detection">Optional detection for calculating pan position.</param>
    /// <param name="screenWidth">Screen width for pan calculation (required if detection is provided).</param>
    /// <param name="interrupt">If true, interrupts any current speech.</param>
    private async Task SpeakWithVoiceAsync(
        string text,
        SpeechTier tier,
        DetectedObject? detection = null,
        int screenWidth = 0,
        bool interrupt = false)
    {
        try
        {
            if (_ttsService == null || !_ttsService.IsReady || string.IsNullOrEmpty(text))
                return;

            var profile = GetSelectedGameProfile();
            if (profile == null)
                return;

            // Set voice and rate based on tier, and check if directional audio is enabled
            bool useDirectionalAudio = false;
            switch (tier)
            {
                case SpeechTier.Primary:
                    _ttsService.SetVoice(profile.Tts.PrimaryVoice ?? "");
                    _ttsService.SetRate(profile.Tts.PrimaryRate);
                    useDirectionalAudio = profile.Tts.PrimaryDirectionalAudio;
                    break;
                case SpeechTier.Secondary:
                    _ttsService.SetVoice(profile.Tts.SecondaryVoice ?? "");
                    _ttsService.SetRate(profile.Tts.SecondaryRate);
                    useDirectionalAudio = profile.Tts.SecondaryDirectionalAudio;
                    break;
                case SpeechTier.Tertiary:
                    _ttsService.SetVoice(profile.Tts.TertiaryVoice ?? "");
                    _ttsService.SetRate(profile.Tts.TertiaryRate);
                    useDirectionalAudio = profile.Tts.TertiaryDirectionalAudio;
                    break;
            }

            // Calculate pan value based on detection position
            float pan = 0.0f;
            if (useDirectionalAudio && detection != null && screenWidth > 0)
            {
                // Use bounding box center for pan calculation
                // Formula: (centerX / screenWidth) * 2 - 1
                // Results: left edge = -1, center = 0, right edge = +1
                pan = (detection.CenterX / screenWidth) * 2f - 1f;
                pan = Math.Clamp(pan, -1.0f, 1.0f);
                Logger.Log($"Directional audio: CenterX={detection.CenterX:F0}, Width={screenWidth}, Pan={pan:F2}");
            }

            // Always use panned speech (pan=0 for center when directional disabled)
            await _ttsService.SpeakWithPanAsync(text, pan, interrupt);
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
        LoadOverlaySettings(); // Load overlay settings for the new game profile
        LoadCrosshairSettings(); // Load crosshair settings for the new game profile
        LoadTrainingSettings(); // Load training settings for the new game profile

        // Save the selection
        Task.Run(async () => await _configManager.SaveAppSettingsAsync(_appConfig));
    }

    /// <summary>
    /// Loads overlay settings from the current game profile.
    /// </summary>
    private void LoadOverlaySettings()
    {
        var profile = GetSelectedGameProfile();
        if (profile?.Overlay == null) return;

        OverlayToggleHotkey = profile.Overlay.ToggleHotkey;

        OverlayGroups.Clear();
        foreach (var group in profile.Overlay.Groups)
        {
            OverlayGroups.Add(group);
        }
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
    private async Task ToggleEngineAsync()
    {
        if (IsEngineRunning)
        {
            StopEngine();
        }
        else
        {
            await StartEngineAsync();
        }
    }

    private async Task StartEngineAsync()
    {
        Logger.Log("Starting engine");
        var profile = GetSelectedGameProfile();
        if (profile == null)
        {
            Logger.Warn("No game profile selected");
            DetectionStatus = "No game selected";
            return;
        }

        // Initialize detection manager (only subscribe to DetectionsReady for UI updates)
        _detectionManager?.Dispose();
        _detectionManager = new DetectionManager();
        _detectionManager.DetectionsReady += OnDetectionsReady;

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

        // Initialize capture manager
        _captureManager?.Dispose();
        _captureManager = new ScreenCaptureManager();
        _captureManager.FrameCaptured += OnFrameCaptured;
        _captureManager.WindowFound += OnWindowFound;
        _captureManager.WindowLost += OnWindowLost;

        DetectionStatus = "Finding window...";

        if (!_captureManager.Initialize(profile))
        {
            DetectionStatus = "Failed to initialize capture";
            return;
        }

        // Check if we're waiting for a window to appear
        if (_captureManager.IsWaitingForWindow)
        {
            IsEngineRunning = true;
            IsWaitingForWindow = true;
            DetectionStatus = $"Waiting for: {profile.WindowTitle}";
            _frameCount = 0;
            _detectionCount = 0;
            CurrentDetectionCount = 0;

            // Initialize training data manager
            InitializeTrainingDataManager(profile);

            // Start window polling loop
            StartWindowPolling();

            Logger.Log($"Engine started in waiting mode for window: {profile.WindowTitle}");
        }
        else if (await _captureManager.StartAsync())
        {
            IsEngineRunning = true;
            IsWaitingForWindow = false;
            DetectionStatus = "Running";
            _frameCount = 0;
            _detectionCount = 0;
            CurrentDetectionCount = 0;

            // Initialize training data manager
            InitializeTrainingDataManager(profile);

            // Start window monitoring (for window loss detection)
            StartWindowPolling();

            // Enable features based on saved settings
            if (IsScreenReaderEnabled)
            {
                await EnableScreenReaderAsync();
            }
            if (IsOverlayEnabled)
            {
                EnableOverlay();
            }
            if (IsCrosshairEnabled)
            {
                EnableCrosshair();
            }

            // Start waypoint timer if configured
            InitializeWaypointTracker(profile);
        }
        else
        {
            DetectionStatus = "Failed to start capture";
        }
    }

    /// <summary>
    /// Initializes the waypoint tracker timer.
    /// </summary>
    private void InitializeWaypointTracker(GameProfile profile)
    {
        StopWaypointTracker();

        _waypointSettings = profile.Waypoint;
        if (_waypointSettings == null || !_waypointSettings.Enabled ||
            string.IsNullOrEmpty(_waypointSettings.Label))
        {
            Logger.Log("WaypointTracker: Not enabled or no label configured");
            return;
        }

        var intervalMs = _waypointSettings.ReadIntervalSeconds * 1000;
        _waypointTimer = new System.Timers.Timer(intervalMs);
        _waypointTimer.Elapsed += OnWaypointTimerElapsed;

        // For read mode: auto-reset so it fires at regular intervals
        // For sonar mode: no auto-reset, we manually restart after beeping
        _waypointTimer.AutoReset = _waypointSettings.Mode != "sonar";
        _waypointTimer.Start();

        var modeDesc = _waypointSettings.Mode == "sonar" ? "sonar" : "read";
        Logger.Log($"WaypointTracker: Started ({modeDesc}) for '{_waypointSettings.Label}' @ {_waypointSettings.ReadIntervalSeconds}s");
    }

    /// <summary>
    /// Stops the waypoint tracker timer.
    /// </summary>
    private void StopWaypointTracker()
    {
        _sonarArmed = false;

        if (_waypointTimer != null)
        {
            _waypointTimer.Stop();
            _waypointTimer.Elapsed -= OnWaypointTimerElapsed;
            _waypointTimer.Dispose();
            _waypointTimer = null;
        }
        _waypointSettings = null;
    }

    /// <summary>
    /// Handles the waypoint timer elapsed event.
    /// </summary>
    private async void OnWaypointTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_waypointSettings?.Mode == "sonar")
        {
            // Sonar mode: arm the beep, actual beep happens on next detection
            _sonarArmed = true;
            Logger.Log("WaypointTracker: Sonar armed");
        }
        else
        {
            // Read mode: read the waypoint immediately
            await ReadWaypointAsync();
        }
    }

    /// <summary>
    /// Reads the waypoint label via OCR and speaks it.
    /// </summary>
    private async Task ReadWaypointAsync()
    {
        if (_detectionManager == null || _waypointSettings == null || _ttsService == null || !_ttsService.IsReady)
            return;

        // Skip if we're waiting for window
        if (IsWaitingForWindow)
            return;

        var waypointLabel = _waypointSettings.Label;

        // Get the waypoint detection
        var waypointDetection = _detectionManager.GetWaypointDetection(waypointLabel);
        if (waypointDetection == null)
        {
            // Waypoint not detected - skip silently
            return;
        }

        // Get frame for OCR
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null || frame.IsDisposed || _ocrService == null || !_ocrService.IsReady)
            return;

        try
        {
            // Run OCR on waypoint bounding box
            var regions = new List<OcrRegion> { OcrRegion.FromDetection(waypointDetection) };
            var textResults = await _ocrService.ExtractTextFromRegionsAsync(
                frame.Data, frame.Width, frame.Height, frame.Stride, regions);

            if (textResults.TryGetValue(waypointLabel, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                Logger.Log($"WaypointTracker: Reading '{text}'");

                // Speak with primary voice (don't interrupt current speech)
                if (!_ttsService.IsSpeaking)
                {
                    await SpeakWithVoiceAsync(text, SpeechTier.Primary, waypointDetection, frame.Width, interrupt: false);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("WaypointTracker: Error reading waypoint", ex);
        }
    }

    /// <summary>
    /// Starts the window polling loop for waiting/recovery.
    /// </summary>
    private void StartWindowPolling()
    {
        StopWindowPolling();

        _windowPollingCts = new CancellationTokenSource();
        _windowPollingTask = Task.Run(() => WindowPollingLoopAsync(_windowPollingCts.Token));
    }

    /// <summary>
    /// Stops the window polling loop.
    /// </summary>
    private void StopWindowPolling()
    {
        _windowPollingCts?.Cancel();
        _windowPollingCts?.Dispose();
        _windowPollingCts = null;
        _windowPollingTask = null;
    }

    /// <summary>
    /// Background loop that polls for window availability and health.
    /// </summary>
    private async Task WindowPollingLoopAsync(CancellationToken cancellationToken)
    {
        const int pollingIntervalMs = 1500; // Check every 1.5 seconds

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollingIntervalMs, cancellationToken);

                if (_captureManager == null)
                    break;

                if (_captureManager.IsWaitingForWindow)
                {
                    // Try to find the window
                    if (_captureManager.TryFindWindow())
                    {
                        // Window was found - start capture
                        Logger.Log("WindowPollingLoop: Window found, starting capture");
                        // The WindowFound event handler will be called by TryFindWindow
                    }
                }
                else if (_captureManager.IsCapturing)
                {
                    // Check if window is still valid
                    if (_captureManager.RequiresWindow)
                    {
                        // Also check for stalled capture (no frames arriving)
                        if (_captureManager.IsCaptureStalled(3.0))
                        {
                            Logger.Log("WindowPollingLoop: Capture appears stalled, checking window");
                            _captureManager.CheckWindowStillValid();
                        }
                        else
                        {
                            // Periodically validate window is still there
                            _captureManager.CheckWindowStillValid();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error("WindowPollingLoop error", ex);
            }
        }
    }

    /// <summary>
    /// Handles the WindowFound event from ScreenCaptureManager.
    /// </summary>
    private async void OnWindowFound(object? sender, EventArgs e)
    {
        Logger.Log("OnWindowFound: Window appeared, starting capture");

        try
        {
            if (_captureManager != null && await _captureManager.StartAsync())
            {
                // Update UI on dispatcher thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    IsWaitingForWindow = false;
                    DetectionStatus = "Running";

                    // Enable features based on saved settings
                    if (IsScreenReaderEnabled)
                    {
                        await EnableScreenReaderAsync();
                    }
                    if (IsOverlayEnabled)
                    {
                        EnableOverlay();
                    }
                    if (IsCrosshairEnabled)
                    {
                        EnableCrosshair();
                    }
                });

                Logger.Log("OnWindowFound: Capture started successfully");
            }
            else
            {
                Logger.Warn("OnWindowFound: Failed to start capture after window found");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DetectionStatus = "Failed to start capture";
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnWindowFound error", ex);
        }
    }

    /// <summary>
    /// Handles the WindowLost event from ScreenCaptureManager.
    /// </summary>
    private void OnWindowLost(object? sender, EventArgs e)
    {
        Logger.Log("OnWindowLost: Window disappeared, entering waiting mode");

        try
        {
            // Update UI on dispatcher thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsWaitingForWindow = true;
                var profile = GetSelectedGameProfile();
                DetectionStatus = $"Waiting for: {profile?.WindowTitle ?? "window"}";
                CurrentDetectionCount = 0;

                // Disable overlay while waiting (no frames to render)
                if (IsOverlayEnabled && _overlayWindow != null)
                {
                    _overlayWindow.Hide();
                }

                // Hide crosshair while waiting
                if (IsCrosshairEnabled && _crosshairWindow != null)
                {
                    _crosshairWindow.Hide();
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("OnWindowLost error", ex);
        }
    }

    private void StopEngine()
    {
        Logger.Log("Stopping engine");

        // Stop timers first
        StopWindowPolling();
        StopWaypointTracker();

        // Disable all features first
        DisableScreenReader();
        DisableOverlay();
        DisableCrosshair();

        // Stop capture and detection
        if (_captureManager != null)
        {
            _captureManager.FrameCaptured -= OnFrameCaptured;
            _captureManager.WindowFound -= OnWindowFound;
            _captureManager.WindowLost -= OnWindowLost;
            _captureManager.Stop();
        }

        if (_detectionManager != null)
        {
            _detectionManager.DetectionsReady -= OnDetectionsReady;
            _detectionManager.Dispose();
            _detectionManager = null;
        }

        IsEngineRunning = false;
        IsWaitingForWindow = false;
        DetectionStatus = "Stopped";
        CurrentDetectionCount = 0;
    }

    private async void OnFrameCaptured(object? sender, CapturedFrame e)
    {
        try
        {
            _frameCount++;

            // Update frame timing for window loss detection
            _captureManager?.UpdateLastFrameTime();

            // Extract frame ID and capture start for performance tracking
            var frameId = e.FrameId;
            var captureStartTicks = e.CaptureStartTicks;

            // Store frame dimensions before they might become invalid
            var frameWidth = e.Width;
            var frameHeight = e.Height;

            // Log frame received with timing
            if (frameId > 0)
            {
                Logger.PerfFrameTimed(frameId, captureStartTicks, "PIPELINE", "Frame received by MainViewModel");
            }
            else if (_frameCount % 100 == 1)
            {
                Logger.Log($"OnFrameCaptured: Frame {_frameCount}, {frameWidth}x{frameHeight}, disposed={e.IsDisposed}");
            }

            // Store latest frame for OCR processing
            lock (_frameLock)
            {
                _latestFrame = e;
            }

            var detectionService = _detectionManager?.DetectionService;
            if (detectionService == null || !detectionService.IsReady)
            {
                if (_frameCount % 100 == 1)
                {
                    Logger.Warn($"OnFrameCaptured: Detection not ready");
                }
                return;
            }

            // Determine which paths need to run
            bool overlayNeedsUpdate = IsOverlayRunning && _overlayRenderer != null && _overlayVisible;
            bool screenReaderNeedsUpdate = IsScreenReaderEnabled;

            // Fast path: Run detection once, use results for both overlay and screen reader
            if (overlayNeedsUpdate)
            {
                var detectStartTicks = Stopwatch.GetTimestamp();
                try
                {
                    // Use low threshold for overlay (per-group filtering happens in render)
                    var detections = await detectionService.DetectAsync(e, 0.1f);
                    var detectMs = (double)(Stopwatch.GetTimestamp() - detectStartTicks) / Stopwatch.Frequency * 1000.0;

                    if (detections != null)
                    {
                        // Successful inference - render immediately
                        _detectionCount = detections.Count;

                        var detectionsForRender = detections;
                        var dispatchStartTicks = Stopwatch.GetTimestamp();

                        if (frameId > 0)
                        {
                            Logger.PerfFrameTimed(frameId, captureStartTicks, "RENDER", "Dispatcher queued");
                        }

                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Render,
                            () =>
                            {
                                var dispatchMs = (double)(Stopwatch.GetTimestamp() - dispatchStartTicks) / Stopwatch.Frequency * 1000.0;
                                if (frameId > 0)
                                {
                                    Logger.PerfFrameTimed(frameId, captureStartTicks, "RENDER", $"Dispatcher executed ({dispatchMs:F1}ms wait)");
                                }

                                var renderStartTicks = Stopwatch.GetTimestamp();
                                RenderOverlayDetections(detectionsForRender, frameId, captureStartTicks);
                                var renderMs = (double)(Stopwatch.GetTimestamp() - renderStartTicks) / Stopwatch.Frequency * 1000.0;

                                // Calculate total pipeline time from frame capture start
                                if (frameId > 0 && captureStartTicks > 0)
                                {
                                    var totalMs = (double)(Stopwatch.GetTimestamp() - captureStartTicks) / Stopwatch.Frequency * 1000.0;
                                    // Calculate capture time (from start to when frame was received)
                                    // This is approximate since we don't have the exact capture end time here
                                    var captureMs = totalMs - detectMs - dispatchMs - renderMs;
                                    Logger.PerfFrameSummary(frameId, captureMs, detectMs, dispatchMs, renderMs, totalMs);
                                }
                            });

                        // If screen reader is also enabled, process for TTS events (on separate task to not block)
                        if (screenReaderNeedsUpdate && _detectionManager != null)
                        {
                            // Fire-and-forget: let DetectionManager process for TTS
                            _ = _detectionManager.ProcessFrameAsync(e);
                        }
                    }
                    else
                    {
                        // Inference was skipped (still processing previous frame)
                        // Clear the overlay so stale boxes don't persist at wrong positions
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Render,
                            () => _overlayRenderer?.Clear());

                        if (frameId > 0)
                        {
                            Logger.PerfFrame(frameId, "PIPELINE", "SKIPPED (inference busy)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Overlay detection error", ex);
                }
            }
            // Screen reader only path (no overlay)
            else if (screenReaderNeedsUpdate && _detectionManager != null)
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

            // Update UI every 30 frames (roughly 1 second at 30 FPS)
            if (_frameCount % 30 == 0)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
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

    private async void OnDetectionsReady(object? sender, DetectionEventArgs e)
    {
        // Note: Overlay rendering is now handled directly in OnFrameCaptured for better performance.
        // This event is kept for TTS/Screen Reader functionality which subscribes to PrimaryObjectChanged.

        // Check for sonar mode waypoint beep
        if (_sonarArmed && _waypointSettings?.Mode == "sonar")
        {
            await CheckAndPlaySonarBeepAsync();
        }
    }

    /// <summary>
    /// Checks for waypoint detection and plays sonar beep if armed.
    /// </summary>
    private async Task CheckAndPlaySonarBeepAsync()
    {
        if (_detectionManager == null || _waypointSettings == null || _ttsService == null)
            return;

        // Skip if waiting for window
        if (IsWaitingForWindow)
            return;

        var waypointLabel = _waypointSettings.Label;

        // Get waypoint detection (uses narrowest width for sonar mode)
        var waypointDetection = _detectionManager.GetSonarWaypointDetection(waypointLabel);
        if (waypointDetection == null)
        {
            // No waypoint detected above threshold, stay armed and wait
            Logger.LogDebug($"WaypointTracker: Sonar armed but no waypoint '{waypointLabel}' above threshold");
            return;
        }

        // Get frame dimensions for pan calculation BEFORE disarming
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null || frame.IsDisposed)
        {
            // Can't get frame dimensions, stay armed and try next detection
            Logger.LogDebug("WaypointTracker: Sonar armed but frame unavailable");
            return;
        }

        // Now we have everything we need - disarm and play beep
        _sonarArmed = false;

        // Calculate pan based on waypoint position
        // Center 15% of screen = both speakers equally (pan = 0)
        // Left of center = left speaker ONLY (pan = -1)
        // Right of center = right speaker ONLY (pan = 1)
        float normalizedX = waypointDetection.CenterX / (float)frame.Width;
        float pan;

        const float centerZone = 0.15f; // 15% center zone
        float centerStart = 0.5f - (centerZone / 2f); // 0.425
        float centerEnd = 0.5f + (centerZone / 2f);   // 0.575

        if (normalizedX >= centerStart && normalizedX <= centerEnd)
        {
            // In center zone - play in both speakers equally
            pan = 0f;
        }
        else if (normalizedX < centerStart)
        {
            // Left side - full left speaker only
            pan = -1f;
        }
        else
        {
            // Right side - full right speaker only
            pan = 1f;
        }

        Logger.Log($"WaypointTracker: Sonar beep at X={normalizedX:F2}, pan={pan:F2}");

        // Play the beep
        await _ttsService.PlayBeepWithPanAsync(pan);

        // Restart the timer for next interval
        _waypointTimer?.Start();
        Logger.LogDebug("WaypointTracker: Timer restarted for next sonar interval");
    }

    private void OnLabelDisappeared(object? sender, LabelDisappearedEventArgs e)
    {
        // Label disappeared or moved to different object - cancel any ongoing TTS
        Logger.Log($"Label disappeared: {e.Label} (MovedToNew: {e.MovedToNewObject}, FramesMissing: {e.FramesMissing})");

        // Cancel current speech and clear queue
        _ttsService?.Stop();
        _ttsService?.ClearQueue();

        // Reset position tracking so the next detection triggers auto-read
        _detectionManager?.ResetPositionTracking();
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
                    // Auto-read should NOT interrupt current speech - skip if TTS is busy
                    // Manual reads (hotkeys) will interrupt, but auto-reads should wait/skip
                    if (_ttsService.IsSpeaking)
                    {
                        Logger.Log($"Auto-read: Skipping '{detection.Label}' - TTS is busy");
                    }
                    else
                    {
                        // Start tracking the label being read so we can cancel if user moves away
                        _detectionManager?.StartTrackingLabel(detection.Label, detection);

                        // Auto-read uses interrupt: false - should not interrupt anything
                        await SpeakWithVoiceAsync(speechText, SpeechTier.Primary, detection, frame.Width, interrupt: false);

                        // Stop tracking after speech completes (if not already stopped due to disappearance)
                        _detectionManager?.StopTrackingLabel(detection.Label);
                    }
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
        // Stop detection before changing settings (but keep crosshair for live preview)
        var wasRunning = IsEngineRunning;
        var crosshairWasRunning = IsCrosshairRunning;

        if (wasRunning)
        {
            // Disable screen reader and overlay, but keep crosshair for preview
            DisableScreenReader();
            DisableOverlay();

            // Stop capture and detection
            StopWindowPolling();
            StopWaypointTracker();
            _captureManager?.Stop();
            IsEngineRunning = false;
            DetectionStatus = "Settings";
        }

        // Create callback for live crosshair preview updates
        Action<CrosshairSettings>? crosshairPreviewCallback = null;
        if (crosshairWasRunning && _crosshairRenderer != null)
        {
            crosshairPreviewCallback = (settings) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _crosshairRenderer?.UpdateSettings(settings);
                });
            };
        }

        var settingsWindow = new Views.GameSettingsWindow(_appConfig, _configManager, crosshairPreviewCallback);
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
                await StartEngineAsync();
            }
        }
        else
        {
            // User cancelled - restore crosshair to original settings
            if (crosshairWasRunning)
            {
                var profile = GetSelectedGameProfile();
                var settings = profile?.Crosshair ?? new CrosshairSettings();
                _crosshairRenderer?.UpdateSettings(settings);
            }
        }

        // Clean up crosshair if engine is not restarting
        if (!wasRunning && crosshairWasRunning)
        {
            DisableCrosshair();
        }
    }

    [RelayCommand]
    private async Task OpenAppSettingsAsync()
    {
        // Stop detection before changing settings
        var wasRunning = IsEngineRunning;
        if (wasRunning)
        {
            StopEngine();
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
                await StartEngineAsync();
            }
        }
    }

    #region Screen Reader Enable/Disable

    private async Task EnableScreenReaderAsync()
    {
        if (!IsEngineRunning) return;

        Logger.Log("Enabling screen reader");
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

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
            _ttsService.SetRate(profile.Tts.PrimaryRate);
            _ttsService.SetVolume(profile.Tts.Volume);

            if (!string.IsNullOrEmpty(profile.Tts.PrimaryVoice))
            {
                _ttsService.SetVoice(profile.Tts.PrimaryVoice);
            }

            System.Diagnostics.Debug.WriteLine("TTS service initialized");
        }

        // Subscribe to detection events for TTS auto-read
        if (_detectionManager != null)
        {
            _detectionManager.PrimaryObjectChanged += OnPrimaryObjectChanged;
            _detectionManager.LabelDisappeared += OnLabelDisappeared;
        }

        Logger.Log("Screen reader enabled");
    }

    private void DisableScreenReader()
    {
        Logger.Log("Disabling screen reader");

        _ttsService?.Stop();
        _ttsService?.ClearQueue();
        _ttsService?.Dispose();
        _ttsService = null;

        _ocrService?.Dispose();
        _ocrService = null;

        // Unsubscribe from TTS events
        if (_detectionManager != null)
        {
            _detectionManager.PrimaryObjectChanged -= OnPrimaryObjectChanged;
            _detectionManager.LabelDisappeared -= OnLabelDisappeared;
            _detectionManager.StopTrackingAllLabels();
        }

        Logger.Log("Screen reader disabled");
    }

    #endregion

    #region Overlay Enable/Disable

    private void EnableOverlay()
    {
        if (!IsEngineRunning || IsOverlayRunning) return;

        Logger.Log("Enabling overlay");
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        // Create overlay window
        _overlayWindow = new OverlayWindow();
        _overlayRenderer = new OverlayRenderer(_overlayWindow.Canvas);

        // Position on correct monitor (this calculates DPI scale)
        var monitorIndex = profile.Capture?.MonitorIndex ?? 0;
        _overlayWindow.PositionOverMonitor(monitorIndex);
        _overlayWindow.Show();

        // Pass DPI scale to renderer for correct coordinate conversion
        // Must be done after Show() to ensure DPI is properly detected
        _overlayRenderer.DpiScale = _overlayWindow.DpiScale;
        Logger.Log($"Overlay DPI scale set to {_overlayWindow.DpiScale:F2} ({_overlayWindow.DpiScale * 100:F0}%)");

        // Register overlay hotkey
        var hotkey = profile.Overlay?.ToggleHotkey ?? "Alt+O";
        _overlayHotkeyService = new OverlayHotkeyService(hotkey);
        _overlayHotkeyService.HotkeyPressed += OnOverlayHotkeyPressed;
        _overlayHotkeyService.Start();

        _overlayVisible = true;
        IsOverlayRunning = true;
        Logger.Log("Overlay enabled");
    }

    private void DisableOverlay()
    {
        if (_stoppingOverlay || !IsOverlayRunning) return;

        try
        {
            _stoppingOverlay = true;
            Logger.Log("Disabling overlay");

            _overlayHotkeyService?.Stop();
            _overlayHotkeyService?.Dispose();
            _overlayHotkeyService = null;

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _overlayWindow?.Close();
                _overlayWindow = null;
            });

            _overlayRenderer = null;
            IsOverlayRunning = false;

            Logger.Log("Overlay disabled");
        }
        finally
        {
            _stoppingOverlay = false;
        }
    }

    private void OnOverlayHotkeyPressed(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _overlayVisible = !_overlayVisible;
            if (_overlayWindow != null)
            {
                if (_overlayVisible)
                {
                    _overlayWindow.Show();
                }
                else
                {
                    _overlayWindow.Hide();
                }
            }
        });
    }

    /// <summary>
    /// Renders overlay detections on the canvas using batch drawing for performance.
    /// </summary>
    /// <param name="detections">The detections to render.</param>
    /// <param name="frameId">Optional frame ID for performance tracking.</param>
    /// <param name="captureStartTicks">Optional capture start ticks for performance tracking.</param>
    private void RenderOverlayDetections(List<DetectedObject> detections, ulong frameId = 0, long captureStartTicks = 0)
    {
        if (_overlayRenderer == null || !_overlayVisible) return;

        // Build list of (detection, group) pairs for batch rendering
        var items = new List<(DetectedObject detection, OverlayGroup group)>();

        foreach (var group in OverlayGroups)
        {
            foreach (var det in detections)
            {
                if (group.Labels.Contains(det.Label) && det.Confidence >= group.ConfidenceThreshold)
                {
                    items.Add((det, group));
                }
            }
        }

        // Single batch draw call - much faster than individual DrawBox calls
        _overlayRenderer.DrawAll(items, frameId, captureStartTicks);
    }

    [RelayCommand]
    private void AddGroup()
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        // Get all available labels from the model
        var availableLabels = profile.Labels?.Select(l => l.Name).ToList() ?? [];

        var newGroup = new OverlayGroup
        {
            Name = $"Group {OverlayGroups.Count + 1}",
            Color = "#FF0000",
            Thickness = 2,
            ShowLabel = true,
            ConfidenceThreshold = 0.5f,
            Style = "outlined",
            Labels = []
        };

        var editor = new GroupEditorWindow(newGroup, availableLabels);
        editor.Owner = System.Windows.Application.Current.MainWindow;

        if (editor.ShowDialog() == true)
        {
            OverlayGroups.Add(newGroup);
            SaveOverlaySettings();
        }
    }

    [RelayCommand]
    private void EditGroup()
    {
        if (SelectedOverlayGroup == null) return;

        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        var availableLabels = profile.Labels?.Select(l => l.Name).ToList() ?? [];

        var editor = new GroupEditorWindow(SelectedOverlayGroup, availableLabels);
        editor.Owner = System.Windows.Application.Current.MainWindow;

        if (editor.ShowDialog() == true)
        {
            // Trigger UI refresh
            var index = OverlayGroups.IndexOf(SelectedOverlayGroup);
            if (index >= 0)
            {
                OverlayGroups[index] = SelectedOverlayGroup;
            }
            SaveOverlaySettings();
        }
    }

    [RelayCommand]
    private void RemoveGroup()
    {
        if (SelectedOverlayGroup == null) return;

        OverlayGroups.Remove(SelectedOverlayGroup);
        SelectedOverlayGroup = OverlayGroups.FirstOrDefault();
        SaveOverlaySettings();
    }

    /// <summary>
    /// Saves the current overlay settings to the game profile.
    /// </summary>
    private void SaveOverlaySettings()
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        profile.Overlay ??= new OverlaySettings();
        profile.Overlay.ToggleHotkey = OverlayToggleHotkey;
        profile.Overlay.Groups = OverlayGroups.ToList();

        Task.Run(async () => await _configManager.SaveGameProfileAsync(profile));
    }

    #endregion

    #region Crosshair Enable/Disable

    /// <summary>
    /// Loads crosshair settings from the current game profile.
    /// </summary>
    private void LoadCrosshairSettings()
    {
        var profile = GetSelectedGameProfile();
        if (profile?.Crosshair == null)
        {
            CrosshairToggleHotkey = "Alt+C";
            return;
        }

        // Crosshair toggle hotkey is stored in app config, not per-game
        // But we could add per-game hotkey later if needed
    }

    private void EnableCrosshair()
    {
        if (!IsEngineRunning || IsCrosshairRunning) return;

        Logger.Log("Enabling crosshair");
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        // Create crosshair window
        _crosshairWindow = new CrosshairWindow();
        _crosshairRenderer = new CrosshairRenderer(_crosshairWindow.Canvas);

        // Position on correct monitor (matches overlay behavior)
        var monitorIndex = profile.Capture?.MonitorIndex ?? 0;
        _crosshairWindow.PositionOverMonitor(monitorIndex);
        _crosshairWindow.Show();

        // Set DPI scale and draw crosshair
        _crosshairRenderer.DpiScale = _crosshairWindow.DpiScale;

        // Get crosshair settings (use defaults if not configured)
        var settings = profile.Crosshair ?? new CrosshairSettings();
        _crosshairRenderer.UpdateSettings(settings);

        // Register crosshair hotkey
        var hotkey = CrosshairToggleHotkey;
        _crosshairHotkeyService = new CrosshairHotkeyService(hotkey);
        _crosshairHotkeyService.HotkeyPressed += OnCrosshairHotkeyPressed;
        _crosshairHotkeyService.Start();

        _crosshairVisible = true;
        IsCrosshairRunning = true;
        Logger.Log("Crosshair enabled");
    }

    private void DisableCrosshair()
    {
        if (_stoppingCrosshair || !IsCrosshairRunning) return;

        try
        {
            _stoppingCrosshair = true;
            Logger.Log("Disabling crosshair");

            _crosshairHotkeyService?.Stop();
            _crosshairHotkeyService?.Dispose();
            _crosshairHotkeyService = null;

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _crosshairWindow?.Close();
                _crosshairWindow = null;
            });

            _crosshairRenderer = null;
            IsCrosshairRunning = false;

            Logger.Log("Crosshair disabled");
        }
        finally
        {
            _stoppingCrosshair = false;
        }
    }

    private void OnCrosshairHotkeyPressed(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _crosshairVisible = !_crosshairVisible;
            if (_crosshairWindow != null)
            {
                if (_crosshairVisible)
                {
                    _crosshairWindow.Show();
                }
                else
                {
                    _crosshairWindow.Hide();
                }
            }
        });
    }

    /// <summary>
    /// Refreshes the crosshair display with current settings.
    /// Call this after settings are changed in Game Settings.
    /// </summary>
    public void RefreshCrosshair()
    {
        if (!IsCrosshairRunning || _crosshairRenderer == null) return;

        var profile = GetSelectedGameProfile();
        var settings = profile?.Crosshair ?? new CrosshairSettings();
        _crosshairRenderer.UpdateSettings(settings);
    }

    #endregion

    #region Property Change Handlers

    partial void OnIsScreenReaderEnabledChanged(bool value)
    {
        if (IsEngineRunning)
        {
            if (value)
            {
                _ = EnableScreenReaderAsync();
            }
            else
            {
                DisableScreenReader();
            }
        }

        // Save setting
        _appConfig.ScreenReaderEnabled = value;
        _ = _configManager.SaveAppSettingsAsync(_appConfig);
    }

    partial void OnIsOverlayEnabledChanged(bool value)
    {
        if (IsEngineRunning)
        {
            if (value)
            {
                EnableOverlay();
            }
            else
            {
                DisableOverlay();
            }
        }

        // Save setting
        _appConfig.OverlayEnabled = value;
        _ = _configManager.SaveAppSettingsAsync(_appConfig);
    }

    partial void OnIsCrosshairEnabledChanged(bool value)
    {
        if (IsEngineRunning)
        {
            if (value)
            {
                EnableCrosshair();
            }
            else
            {
                DisableCrosshair();
            }
        }

        // Save setting
        _appConfig.CrosshairEnabled = value;
        _ = _configManager.SaveAppSettingsAsync(_appConfig);
    }

    #endregion

    #region Training Commands

    [RelayCommand]
    private async Task CreateNewTrainingProfileAsync()
    {
        // Stop engine before creating new profile
        var wasRunning = IsEngineRunning;
        if (wasRunning)
        {
            StopEngine();
        }

        var createWindow = new CreateProfileWindow(_configManager);
        createWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (createWindow.ShowDialog() == true && createWindow.CreatedProfile != null)
        {
            // Reload profiles to include the new one
            await _configManager.LoadAllGameProfilesAsync();
            LoadGames();

            // Select the newly created profile
            SelectedGame = Games.FirstOrDefault(g => g.Key == createWindow.CreatedProfile.GameId);

            Logger.Log($"New profile created and selected: {createWindow.CreatedProfile.DisplayName}");
        }

        // Restart engine if it was running
        if (wasRunning && SelectedGame != null)
        {
            await StartEngineAsync();
        }
    }

    [RelayCommand]
    private void BrowseTrainingFolder()
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Training Data Folder",
            ShowNewFolderButton = true,
            SelectedPath = GetTrainingDataRoot(profile)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TrainingDataPath = dialog.SelectedPath;

            // Update profile and save
            profile.Training ??= new TrainingSettings();
            profile.Training.DataPath = dialog.SelectedPath;
            Task.Run(async () => await _configManager.SaveGameProfileAsync(profile));

            // Reinitialize training data manager with new path
            InitializeTrainingDataManager(profile);
        }
    }

    /// <summary>
    /// Captures a training screenshot (image only, no detection).
    /// Use Post Process to run detection on captured images.
    /// </summary>
    private Task CaptureTrainingScreenshotAsync()
    {
        if (!IsEngineRunning)
        {
            Logger.Log("Training capture: Engine not running");
            System.Media.SystemSounds.Beep.Play();
            return Task.CompletedTask;
        }

        if (!IsTrainingEnabled)
        {
            Logger.Log("Training capture: Training disabled");
            return Task.CompletedTask;
        }

        if (_captureManager == null || _trainingDataManager == null)
        {
            Logger.Warn("Training capture: Manager not initialized");
            System.Media.SystemSounds.Beep.Play();
            return Task.CompletedTask;
        }

        try
        {
            // Use CaptureFrame which uses PrintWindow to avoid capturing overlays
            var frame = _captureManager.CaptureFrame();
            if (frame == null)
            {
                Logger.Error("Training capture: Failed to capture frame");
                System.Media.SystemSounds.Beep.Play();
                return Task.CompletedTask;
            }

            try
            {
                var filename = _trainingDataManager.GetNextFilename();

                // Save screenshot only (no detection - use Post Process for that)
                _trainingDataManager.SaveScreenshot(frame, filename);

                _trainingCaptureCount++;

                // Update UI with status
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    TrainingStatus = $"Captured: {filename}";
                });

                Logger.Log($"Training capture: {filename}");

                // Play a subtle sound to confirm capture
                System.Media.SystemSounds.Asterisk.Play();
            }
            finally
            {
                frame.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Training capture error", ex);
            System.Media.SystemSounds.Beep.Play();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the training data root path for a profile.
    /// </summary>
    private string GetTrainingDataRoot(GameProfile profile)
    {
        if (!string.IsNullOrEmpty(profile.Training?.DataPath))
        {
            return profile.Training.DataPath;
        }

        return Path.Combine(TrainingDataManager.GetDefaultTrainingDataRoot(), profile.GameId);
    }

    /// <summary>
    /// Initializes the training data manager for a profile.
    /// </summary>
    private void InitializeTrainingDataManager(GameProfile profile)
    {
        var trainingRoot = string.IsNullOrEmpty(profile.Training?.DataPath)
            ? TrainingDataManager.GetDefaultTrainingDataRoot()
            : Path.GetDirectoryName(profile.Training.DataPath) ?? TrainingDataManager.GetDefaultTrainingDataRoot();

        var gameFolder = string.IsNullOrEmpty(profile.Training?.DataPath)
            ? profile.GameId
            : Path.GetFileName(profile.Training.DataPath);

        _trainingDataManager = new TrainingDataManager(trainingRoot, string.IsNullOrEmpty(gameFolder) ? profile.GameId : gameFolder);
        _trainingDataManager.Initialize();

        // Save classes.txt if model is loaded
        if (_detectionManager?.DetectionService != null && _detectionManager.DetectionService.Labels.Count > 0)
        {
            _trainingDataManager.SaveClasses(_detectionManager.DetectionService.Labels);
        }

        Logger.Log($"Training data manager initialized: {_trainingDataManager.ImagesPath}");
    }

    /// <summary>
    /// Loads training settings from the current game profile.
    /// </summary>
    private void LoadTrainingSettings()
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        IsTrainingEnabled = profile.Training?.Enabled ?? true;
        TrainingCaptureHotkey = profile.Training?.CaptureHotkey ?? "F1";
        TrainingDataPath = GetTrainingDataRoot(profile);
        AnnotationConfidenceThreshold = profile.Training?.AnnotationConfidenceThreshold ?? 0.1f;
        _trainingCaptureCount = 0;
        UpdateTrainingStatus();
    }

    partial void OnAnnotationConfidenceThresholdChanged(float value)
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        profile.Training ??= new TrainingSettings();
        profile.Training.AnnotationConfidenceThreshold = value;
        Task.Run(async () => await _configManager.SaveGameProfileAsync(profile));
    }

    /// <summary>
    /// Updates the training status message based on current state.
    /// </summary>
    private void UpdateTrainingStatus()
    {
        if (!IsTrainingEnabled)
        {
            TrainingStatus = "Training Disabled";
        }
        else if (!IsEngineRunning)
        {
            TrainingStatus = "Training Inactive - Start Engine";
        }
        else
        {
            TrainingStatus = $"Ready - Press {TrainingCaptureHotkey} to capture";
        }
    }

    partial void OnIsTrainingEnabledChanged(bool value)
    {
        var profile = GetSelectedGameProfile();
        if (profile == null) return;

        // Update and save profile
        profile.Training ??= new TrainingSettings();
        profile.Training.Enabled = value;
        Task.Run(async () => await _configManager.SaveGameProfileAsync(profile));

        UpdateTrainingStatus();
    }

    partial void OnIsEngineRunningChanged(bool value)
    {
        UpdateTrainingStatus();
    }

    /// <summary>
    /// Command to post-process all training images with the current model.
    /// Runs detection on all images and regenerates annotation files.
    /// </summary>
    [RelayCommand]
    private async Task PostProcessTrainingImagesAsync()
    {
        var profile = GetSelectedGameProfile();
        if (profile == null)
        {
            TrainingStatus = "No game selected";
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (string.IsNullOrEmpty(profile.ModelFile))
        {
            TrainingStatus = "No model configured for this game";
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // Get model path
        var modelPath = _configManager.GetModelPath(profile.GameId, profile.ModelFile);
        if (!File.Exists(modelPath))
        {
            TrainingStatus = $"Model not found: {profile.ModelFile}";
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        // Get training data folder (same logic as InitializeTrainingDataManager)
        var trainingRoot = string.IsNullOrEmpty(profile.Training?.DataPath)
            ? TrainingDataManager.GetDefaultTrainingDataRoot()
            : Path.GetDirectoryName(profile.Training.DataPath) ?? TrainingDataManager.GetDefaultTrainingDataRoot();

        var gameFolder = string.IsNullOrEmpty(profile.Training?.DataPath)
            ? profile.GameId
            : Path.GetFileName(profile.Training.DataPath);

        var trainingDataManager = new TrainingDataManager(trainingRoot, string.IsNullOrEmpty(gameFolder) ? profile.GameId : gameFolder);
        trainingDataManager.Initialize();

        // Get all image files
        var imageFiles = trainingDataManager.GetImageFiles();
        if (imageFiles.Length == 0)
        {
            TrainingStatus = "No images found to process";
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        IsPostProcessing = true;
        PostProcessStatus = "Loading model...";

        try
        {
            // Create dedicated detection service for batch processing
            using var detectionService = new YoloDetectionService();
            var initialized = await detectionService.InitializeAsync(modelPath, _appConfig.UseDirectML);
            if (!initialized)
            {
                TrainingStatus = "Failed to load model for post-processing";
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var labels = detectionService.Labels;
            var threshold = AnnotationConfidenceThreshold;
            var totalImages = imageFiles.Length;
            var processedCount = 0;
            var totalDetections = 0;

            // Save classes.txt file
            trainingDataManager.SaveClasses(labels);

            foreach (var imagePath in imageFiles)
            {
                processedCount++;
                PostProcessStatus = $"Processing {processedCount}/{totalImages}...";

                // Load image as CapturedFrame
                var frame = LoadImageAsCapturedFrame(imagePath);
                if (frame == null)
                {
                    Logger.Warn($"Failed to load image: {imagePath}");
                    continue;
                }

                try
                {
                    // Run detection
                    var detections = await detectionService.DetectAsync(frame, threshold) ?? [];
                    totalDetections += detections.Count;

                    // Save annotations (always overwrite)
                    var filename = Path.GetFileNameWithoutExtension(imagePath);
                    trainingDataManager.SaveAnnotations(detections, filename, frame.Width, frame.Height, labels);
                }
                finally
                {
                    frame.Dispose();
                }

                // Allow UI to update
                await Task.Yield();
            }

            TrainingStatus = $"Post-process complete: {totalImages} images, {totalDetections} detections";
            PostProcessStatus = string.Empty;
            Logger.Log($"Post-processing complete: {totalImages} images, {totalDetections} total detections");
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            Logger.Error("Post-processing error", ex);
            TrainingStatus = "Post-processing failed - see logs";
            System.Media.SystemSounds.Beep.Play();
        }
        finally
        {
            IsPostProcessing = false;
            PostProcessStatus = string.Empty;
        }
    }

    /// <summary>
    /// Loads a JPEG image file as a CapturedFrame for detection.
    /// </summary>
    private static CapturedFrame? LoadImageAsCapturedFrame(string imagePath)
    {
        try
        {
            using var bitmap = new Bitmap(imagePath);
            var width = bitmap.Width;
            var height = bitmap.Height;
            var stride = width * 4; // BGRA format

            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var data = new byte[height * stride];

                // Copy bitmap data row by row (handles different strides)
                for (int y = 0; y < height; y++)
                {
                    int srcOffset = y * bitmapData.Stride;
                    int dstOffset = y * stride;

                    unsafe
                    {
                        byte* src = (byte*)bitmapData.Scan0 + srcOffset;
                        for (int x = 0; x < stride; x++)
                        {
                            data[dstOffset + x] = src[x];
                        }
                    }
                }

                return new CapturedFrame
                {
                    Data = data,
                    Width = width,
                    Height = height,
                    Stride = stride,
                    FrameId = 0,
                    CaptureStartTicks = 0
                };
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load image: {imagePath}", ex);
            return null;
        }
    }

    #endregion

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

        StopEngine();
        _captureManager?.Dispose();
        _detectionManager?.Dispose();
        _ocrService?.Dispose();
        _ttsService?.Dispose();

        // Clean up overlay resources
        _overlayHotkeyService?.Dispose();

        // Clean up crosshair resources
        _crosshairHotkeyService?.Dispose();

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
