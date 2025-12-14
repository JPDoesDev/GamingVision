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
    private string _hotkeyStopReading = "Alt+3";

    [ObservableProperty]
    private string _hotkeyToggleDetection = "Alt+4";

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
        _appConfig = await _configManager.LoadAppSettingsAsync();
        await _configManager.LoadAllGameProfilesAsync();
        LoadGames();
        UpdateGpuInfo();
        RegisterHotkeys();
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
        _hotkeyService.RegisterHotkey(HotkeyId.StopReading, profile.Hotkeys.StopReading);
        _hotkeyService.RegisterHotkey(HotkeyId.ToggleDetection, profile.Hotkeys.ToggleDetection);
        _hotkeyService.RegisterHotkey(HotkeyId.Quit, profile.Hotkeys.Quit);

        System.Diagnostics.Debug.WriteLine("Hotkeys registered");
    }

    /// <summary>
    /// Handles hotkey press events.
    /// </summary>
    private async void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Hotkey pressed: {e.HotkeyId}");

        switch (e.HotkeyId)
        {
            case HotkeyId.ReadPrimary:
                await ReadPrimaryObjectsAsync();
                break;

            case HotkeyId.ReadSecondary:
                await ReadSecondaryObjectsAsync();
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

    /// <summary>
    /// Reads the current primary objects on demand.
    /// </summary>
    private async Task ReadPrimaryObjectsAsync()
    {
        if (_detectionManager == null || _ttsService == null)
            return;

        var primaryDetections = _detectionManager.GetCurrentPrimaryDetections();
        if (primaryDetections.Count == 0)
        {
            await _ttsService.SpeakAsync("No primary objects detected", interrupt: true);
            return;
        }

        // Get frame for OCR
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null || frame.IsDisposed || _ocrService == null || !_ocrService.IsReady)
        {
            var labels = string.Join(", ", primaryDetections.Select(d => d.Label));
            await _ttsService.SpeakAsync(labels, interrupt: true);
            return;
        }

        // Extract text from regions
        var regions = primaryDetections.Select(OcrRegion.FromDetection).ToList();
        var textResults = await _ocrService.ExtractTextFromRegionsAsync(
            frame.Data, frame.Width, frame.Height, frame.Stride, regions);

        var textParts = new List<string>();
        foreach (var detection in primaryDetections)
        {
            if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                textParts.Add($"{detection.Label}, {text}");
            }
            else
            {
                textParts.Add(detection.Label);
            }
        }

        var speechText = string.Join(". ", textParts);
        await _ttsService.SpeakAsync(speechText, interrupt: true);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LastReadText = string.Join(" | ", textParts);
        });
    }

    /// <summary>
    /// Reads the current secondary objects on demand.
    /// </summary>
    private async Task ReadSecondaryObjectsAsync()
    {
        if (_detectionManager == null || _ttsService == null)
            return;

        var secondaryDetections = _detectionManager.GetCurrentSecondaryDetections();
        if (secondaryDetections.Count == 0)
        {
            await _ttsService.SpeakAsync("No secondary objects detected", interrupt: true);
            return;
        }

        // Get frame for OCR
        CapturedFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        if (frame == null || frame.IsDisposed || _ocrService == null || !_ocrService.IsReady)
        {
            var labels = string.Join(", ", secondaryDetections.Select(d => d.Label));
            await _ttsService.SpeakAsync(labels, interrupt: true);
            return;
        }

        // Extract text from regions
        var regions = secondaryDetections.Select(OcrRegion.FromDetection).ToList();
        var textResults = await _ocrService.ExtractTextFromRegionsAsync(
            frame.Data, frame.Width, frame.Height, frame.Stride, regions);

        var textParts = new List<string>();
        foreach (var detection in secondaryDetections)
        {
            if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                textParts.Add($"{detection.Label}, {text}");
            }
            else
            {
                textParts.Add(detection.Label);
            }
        }

        var speechText = string.Join(". ", textParts);
        await _ttsService.SpeakAsync(speechText, interrupt: true);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LastReadText = string.Join(" | ", textParts);
        });
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
        var profile = GetSelectedGameProfile();
        if (profile == null)
        {
            DetectionStatus = "No game selected";
            return;
        }

        // Initialize detection manager
        _detectionManager?.Dispose();
        _detectionManager = new DetectionManager();
        _detectionManager.DetectionsReady += OnDetectionsReady;
        _detectionManager.PrimaryObjectChanged += OnPrimaryObjectChanged;

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
        _captureManager?.Stop();
        _ttsService?.Stop();
        _ttsService?.ClearQueue();

        if (_detectionManager != null)
        {
            _detectionManager.DetectionsReady -= OnDetectionsReady;
            _detectionManager.PrimaryObjectChanged -= OnPrimaryObjectChanged;
        }

        IsDetectionRunning = false;
        DetectionStatus = "Stopped";
        CurrentDetectionCount = 0;
    }

    private async void OnFrameCaptured(object? sender, CapturedFrame e)
    {
        _frameCount++;

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
                System.Diagnostics.Debug.WriteLine($"Detection error: {ex.Message}");
            }
        }

        // Update UI every 10 frames to avoid excessive updates
        if (_frameCount % 10 == 0)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                DetectionStatus = $"Running (Frame {_frameCount}, {e.Width}x{e.Height})";
                CurrentDetectionCount = _detectionCount;
            });
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

    private async void OnPrimaryObjectChanged(object? sender, PrimaryObjectChangedEventArgs e)
    {
        // Primary objects changed - queue for auto-read
        if (e.Detections.Count == 0)
            return;

        var labelsToRead = string.Join(", ", e.Detections.Select(d => d.Label));
        System.Diagnostics.Debug.WriteLine($"Primary objects changed: {labelsToRead}");

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
                LastReadText = labelsToRead;
            });
            return;
        }

        try
        {
            // Extract text from detected regions
            var regions = e.Detections.Select(OcrRegion.FromDetection).ToList();
            var textResults = await _ocrService.ExtractTextFromRegionsAsync(
                frame.Data, frame.Width, frame.Height, frame.Stride, regions);

            // Build combined text result
            var textParts = new List<string>();
            foreach (var detection in e.Detections)
            {
                if (textResults.TryGetValue(detection.Label, out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    textParts.Add($"{detection.Label}: {text}");
                }
                else
                {
                    textParts.Add(detection.Label);
                }
            }

            var combinedText = string.Join(" | ", textParts);
            System.Diagnostics.Debug.WriteLine($"OCR result: {combinedText}");

            // Speak the extracted text
            if (_ttsService != null && _ttsService.IsReady)
            {
                // Build speech-friendly text (join with pauses)
                var speechText = string.Join(". ", textParts.Select(t => t.Replace(":", ",")));
                await _ttsService.SpeakAsync(speechText, interrupt: true);
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LastReadText = combinedText;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR error: {ex.Message}");
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LastReadText = labelsToRead;
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
