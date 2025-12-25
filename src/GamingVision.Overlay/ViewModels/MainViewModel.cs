using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GamingVision.Models;
using GamingVision.Overlay.Rendering;
using GamingVision.Overlay.Services;
using GamingVision.Services.Detection;
using GamingVision.Services.ScreenCapture;
using GamingVision.Utilities;
using static GamingVision.Overlay.Services.OverlayLogger;

namespace GamingVision.Overlay.ViewModels;

/// <summary>
/// Main view model for the overlay configuration window.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Window _window;
    private readonly string _gameModelsDirectory;
    private readonly Dispatcher _dispatcher;

    private GameProfile? _selectedGameProfile;
    private OverlayGroup? _selectedGroup;
    private float _confidenceThreshold = 0.5f;
    private string _toggleHotkey = "Alt+O";
    private bool _isOverlayRunning;

    private OverlayWindow? _overlayWindow;
    private OverlayRenderer? _renderer;
    private GdiCaptureService? _captureService;
    private YoloDetectionService? _detectionService;
    private OverlayHotkeyService? _hotkeyService;
    private bool _overlayVisible = true;
    private volatile bool _stopping;
    private int _frameCount;
    private int _detectionCount;
    private int _drawnCount;
    private DateTime _lastFpsLogTime = DateTime.Now;
    private int _framesProcessedSinceLastLog;
    private readonly System.Diagnostics.Stopwatch _inferenceStopwatch = new();
    private DateTime _lastSuccessfulDetectionTime = DateTime.Now;
    private const int StaleOverlayTimeoutMs = 100; // Clear overlay if no detection for 100ms

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(Window window)
    {
        _window = window;
        _dispatcher = window.Dispatcher;
        _gameModelsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameModels");

        // Initialize commands
        AddGroupCommand = new RelayCommand(AddGroup, () => SelectedGameProfile != null);
        EditGroupCommand = new RelayCommand(EditGroup, () => SelectedGroup != null);
        RemoveGroupCommand = new RelayCommand(RemoveGroup, () => SelectedGroup != null);
        MoveGroupUpCommand = new RelayCommand(MoveGroupUp, () => CanMoveUp());
        MoveGroupDownCommand = new RelayCommand(MoveGroupDown, () => CanMoveDown());
        StartOverlayCommand = new RelayCommand(ToggleOverlay, () => SelectedGameProfile != null);
        SaveCommand = new RelayCommand(Save);
        CloseCommand = new RelayCommand(Close);
        OpenGameSettingsCommand = new RelayCommand(OpenGameSettings, () => SelectedGameProfile != null);

        // Load game profiles
        LoadGameProfiles();
    }

    public ObservableCollection<GameProfile> GameProfiles { get; } = [];
    public ObservableCollection<OverlayGroup> OverlayGroups { get; } = [];

    public GameProfile? SelectedGameProfile
    {
        get => _selectedGameProfile;
        set
        {
            if (_selectedGameProfile != value)
            {
                _selectedGameProfile = value;
                OnPropertyChanged();
                LoadOverlaySettings();
                UpdateCommandStates();
            }
        }
    }

    public OverlayGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup != value)
            {
                _selectedGroup = value;
                OnPropertyChanged();
                UpdateCommandStates();
            }
        }
    }

    public float ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set
        {
            if (_confidenceThreshold != value)
            {
                _confidenceThreshold = value;
                OnPropertyChanged();
            }
        }
    }

    public string ToggleHotkey
    {
        get => _toggleHotkey;
        set
        {
            if (_toggleHotkey != value)
            {
                _toggleHotkey = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsOverlayRunning
    {
        get => _isOverlayRunning;
        set
        {
            if (_isOverlayRunning != value)
            {
                _isOverlayRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartButtonText));
            }
        }
    }

    public string StartButtonText => IsOverlayRunning ? "Stop Overlay" : "Start Overlay";

    // Commands
    public ICommand AddGroupCommand { get; }
    public ICommand EditGroupCommand { get; }
    public ICommand RemoveGroupCommand { get; }
    public ICommand MoveGroupUpCommand { get; }
    public ICommand MoveGroupDownCommand { get; }
    public ICommand StartOverlayCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand OpenGameSettingsCommand { get; }

    private void LoadGameProfiles()
    {
        GameProfiles.Clear();

        if (!Directory.Exists(_gameModelsDirectory))
            return;

        foreach (var gameDir in Directory.GetDirectories(_gameModelsDirectory))
        {
            var configPath = Path.Combine(gameDir, "game_config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var profile = JsonSerializer.Deserialize<GameProfile>(json, JsonOptions);
                    if (profile != null)
                    {
                        profile.GameId = Path.GetFileName(gameDir);
                        GameProfiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading profile from {gameDir}: {ex.Message}");
                }
            }
        }

        if (GameProfiles.Count > 0)
        {
            SelectedGameProfile = GameProfiles[0];
        }
    }

    private void LoadOverlaySettings()
    {
        OverlayGroups.Clear();

        if (_selectedGameProfile?.Overlay != null)
        {
            ConfidenceThreshold = _selectedGameProfile.Overlay.ConfidenceThreshold;
            ToggleHotkey = _selectedGameProfile.Overlay.ToggleHotkey;

            foreach (var group in _selectedGameProfile.Overlay.Groups)
            {
                OverlayGroups.Add(group);
            }
        }
        else
        {
            ConfidenceThreshold = 0.5f;
            ToggleHotkey = "Alt+O";
        }
    }

    private void AddGroup()
    {
        var newGroup = new OverlayGroup
        {
            Name = "New Group",
            Color = "#FF0000",
            Thickness = 2,
            ShowLabel = true,
            Style = "solid"
        };

        var editor = new GroupEditorWindow(newGroup, GetAvailableLabels());
        editor.Owner = _window;

        if (editor.ShowDialog() == true)
        {
            OverlayGroups.Add(editor.Group);
            SelectedGroup = editor.Group;
        }
    }

    private void EditGroup()
    {
        if (SelectedGroup == null) return;

        var editor = new GroupEditorWindow(SelectedGroup.Clone(), GetAvailableLabels());
        editor.Owner = _window;

        if (editor.ShowDialog() == true)
        {
            var index = OverlayGroups.IndexOf(SelectedGroup);
            if (index >= 0)
            {
                OverlayGroups[index] = editor.Group;
                SelectedGroup = editor.Group;
            }
        }
    }

    private void RemoveGroup()
    {
        if (SelectedGroup != null)
        {
            var index = OverlayGroups.IndexOf(SelectedGroup);
            OverlayGroups.Remove(SelectedGroup);

            if (OverlayGroups.Count > 0)
            {
                SelectedGroup = OverlayGroups[Math.Min(index, OverlayGroups.Count - 1)];
            }
        }
    }

    private bool CanMoveUp() => SelectedGroup != null && OverlayGroups.IndexOf(SelectedGroup) > 0;
    private bool CanMoveDown() => SelectedGroup != null && OverlayGroups.IndexOf(SelectedGroup) < OverlayGroups.Count - 1;

    private void MoveGroupUp()
    {
        if (SelectedGroup == null) return;
        var index = OverlayGroups.IndexOf(SelectedGroup);
        if (index > 0)
        {
            OverlayGroups.Move(index, index - 1);
        }
    }

    private void MoveGroupDown()
    {
        if (SelectedGroup == null) return;
        var index = OverlayGroups.IndexOf(SelectedGroup);
        if (index < OverlayGroups.Count - 1)
        {
            OverlayGroups.Move(index, index + 1);
        }
    }

    private List<string> GetAvailableLabels()
    {
        if (_selectedGameProfile == null)
            return [];

        return _selectedGameProfile.Labels.Select(l => l.Name).ToList();
    }

    private async void ToggleOverlay()
    {
        try
        {
            OverlayLogger.Log("ToggleOverlay", $"Toggle requested. IsOverlayRunning={IsOverlayRunning}");
            if (IsOverlayRunning)
            {
                OverlayLogger.Log("ToggleOverlay", "Calling StopOverlay...");
                StopOverlay();
                OverlayLogger.Log("ToggleOverlay", "StopOverlay returned.");
            }
            else
            {
                OverlayLogger.Log("ToggleOverlay", "Calling StartOverlayAsync...");
                await StartOverlayAsync();
                OverlayLogger.Log("ToggleOverlay", "StartOverlayAsync returned.");
            }
            OverlayLogger.Log("ToggleOverlay", "Toggle complete.");
        }
        catch (Exception ex)
        {
            OverlayLogger.Log("ERROR", $"Exception in ToggleOverlay: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task StartOverlayAsync()
    {
        if (_selectedGameProfile == null) return;

        try
        {
            // Initialize capture service for fullscreen capture
            _captureService = new GdiCaptureService();
            var monitorIndex = _selectedGameProfile.Capture?.MonitorIndex ?? 0;
            _captureService.InitializeForMonitor(monitorIndex);
            _captureService.SetCaptureInterval(33); // ~30 FPS target (frames skipped during inference)
            Log("Capture", "Capture interval set to 33ms (~30 FPS target)");

            // Initialize detection service
            _detectionService = new YoloDetectionService();
            var modelPath = Path.Combine(_gameModelsDirectory, _selectedGameProfile.GameId, _selectedGameProfile.ModelFile);

            // Load app settings to check GPU preference
            var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");
            bool useGpu = true;
            if (File.Exists(appSettingsPath))
            {
                try
                {
                    var appJson = File.ReadAllText(appSettingsPath);
                    var appConfig = JsonSerializer.Deserialize<AppConfiguration>(appJson, JsonOptions);
                    useGpu = appConfig?.UseDirectML ?? true;
                    Log("Config", $"Loaded app_settings.json: useDirectML={useGpu}");
                }
                catch (Exception ex)
                {
                    Log("Config", $"Failed to load app_settings.json: {ex.Message}, defaulting to GPU=true");
                }
            }
            else
            {
                Log("Config", "No app_settings.json found, defaulting to GPU=true");
            }

            Log("Detection", $"Initializing ONNX model with GPU={useGpu}...");
            if (!await _detectionService.InitializeAsync(modelPath, useGpu))
            {
                Log("ERROR", $"Failed to load model: {_selectedGameProfile.ModelFile}");
                MessageBox.Show($"Could not load model: {_selectedGameProfile.ModelFile}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _captureService.Dispose();
                _captureService = null;
                return;
            }
            Log("Detection", $"Model loaded successfully. Execution provider: {_detectionService.ExecutionProvider}");

            // Create and show overlay window, positioned on the same monitor as capture
            _overlayWindow = new OverlayWindow();
            _overlayWindow.PositionOverMonitor(monitorIndex);
            _renderer = new OverlayRenderer(_overlayWindow.Canvas);
            _overlayWindow.Show();

            Log("Overlay", $"Started on monitor {monitorIndex}, model: {_selectedGameProfile.ModelFile}");

            // Subscribe to frame capture events
            _captureService.FrameCaptured += OnFrameCaptured;

            // Initialize hotkey service
            _hotkeyService = new OverlayHotkeyService(ToggleHotkey);
            _hotkeyService.HotkeyPressed += OnToggleHotkeyPressed;
            _hotkeyService.Start();

            // Start capture
            await _captureService.StartCaptureAsync();

            IsOverlayRunning = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error starting overlay: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StopOverlay();
        }
    }

    private void StopOverlay()
    {
        try
        {
            OverlayLogger.Log("StopOverlay", "Starting overlay shutdown...");
            _stopping = true;

            OverlayLogger.Log("StopOverlay", "Stopping hotkey service...");
            _hotkeyService?.Stop();
            _hotkeyService = null;

            if (_captureService != null)
            {
                OverlayLogger.Log("StopOverlay", "Stopping capture service...");
                _captureService.FrameCaptured -= OnFrameCaptured;
                _captureService.StopCapture();
                _captureService.Dispose();
                _captureService = null;

                // Wait briefly for any in-flight frame processing to complete
                OverlayLogger.Log("StopOverlay", "Waiting for in-flight frames...");
                Thread.Sleep(100);
            }

            OverlayLogger.Log("StopOverlay", "Disposing detection service...");
            try
            {
                _detectionService?.Dispose();
            }
            catch (Exception ex)
            {
                OverlayLogger.Log("StopOverlay", $"Detection service dispose error (ignored): {ex.Message}");
            }
            _detectionService = null;

            OverlayLogger.Log("StopOverlay", "Closing overlay window...");
            _overlayWindow?.Close();
            _overlayWindow = null;
            _renderer = null;

            OverlayLogger.Log("StopOverlay", "Setting IsOverlayRunning to false...");
            IsOverlayRunning = false;
            _stopping = false;

            OverlayLogger.Log("StopOverlay", "Overlay shutdown complete.");
        }
        catch (Exception ex)
        {
            OverlayLogger.Log("ERROR", $"Exception in StopOverlay: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void OnFrameCaptured(object? sender, CapturedFrame frame)
    {
        if (_stopping || _detectionService == null || _renderer == null || !_overlayVisible)
            return;

        try
        {
            _frameCount++;
            _framesProcessedSinceLastLog++;

            // Track inference time
            _inferenceStopwatch.Restart();

            // Use a very low base threshold, then filter by per-group confidence
            var detections = await _detectionService.DetectAsync(frame, 0.01f);

            _inferenceStopwatch.Stop();
            var inferenceMs = _inferenceStopwatch.ElapsedMilliseconds;

            // If frame was skipped (inference still running), check if we should clear stale overlay
            if (detections == null)
            {
                var msSinceLastDetection = (DateTime.Now - _lastSuccessfulDetectionTime).TotalMilliseconds;
                if (msSinceLastDetection > StaleOverlayTimeoutMs)
                {
                    _dispatcher.Invoke(() =>
                    {
                        if (_renderer != null)
                            _renderer.Clear();
                    });
                }
                return;
            }

            _lastSuccessfulDetectionTime = DateTime.Now;
            _detectionCount = detections.Count;

            // Calculate and log FPS every 5 seconds
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsLogTime).TotalSeconds;
            if (elapsed >= 5.0)
            {
                var fps = _framesProcessedSinceLastLog / elapsed;
                Log("FPS", $"Overlay running at {fps:F1} FPS (inference: {inferenceMs}ms per frame)");
                _framesProcessedSinceLastLog = 0;
                _lastFpsLogTime = now;
            }

            // Log diagnostics every 30 frames
            if (_frameCount % 30 == 1)
            {
                Log("Detection", $"Frame {_frameCount}: {detections.Count} detections, {_drawnCount} drawn last frame");
                Log("Detection", $"Frame size: {frame.Width}x{frame.Height}");
                Log("Detection", $"Configured groups: {OverlayGroups.Count}");
                foreach (var g in OverlayGroups)
                {
                    Log("Detection", $"  Group '{g.Name}': labels=[{string.Join(", ", g.Labels)}], threshold={g.ConfidenceThreshold}");
                }
            }

            _dispatcher.Invoke(() =>
            {
                // Check if overlay was stopped while we were processing
                if (_renderer == null || _overlayWindow == null)
                    return;

                _renderer.Clear();
                var drawnThisFrame = 0;

                foreach (var detection in detections)
                {
                    // Log each detection for debugging
                    if (_frameCount % 30 == 1)
                    {
                        Log("Detection", $"Found: label='{detection.Label}', conf={detection.Confidence:F3}, box=({detection.X1},{detection.Y1})-({detection.X2},{detection.Y2})");
                    }

                    // Find matching group for this label
                    var group = OverlayGroups.FirstOrDefault(g =>
                        g.Labels.Contains(detection.Label, StringComparer.OrdinalIgnoreCase));

                    if (group == null)
                    {
                        // No group configured for this label - log warning
                        if (_frameCount % 30 == 1)
                        {
                            Log("WARNING", $"No group found for label '{detection.Label}' - detection will not be drawn!");
                        }
                        continue;
                    }

                    // Check if detection meets the group's confidence threshold
                    if (detection.Confidence < group.ConfidenceThreshold)
                    {
                        if (_frameCount % 30 == 1)
                        {
                            Log("Detection", $"Filtered: '{detection.Label}' conf={detection.Confidence:F3} < threshold={group.ConfidenceThreshold}");
                        }
                        continue;
                    }

                    _renderer.DrawBox(
                        detection.X1,
                        detection.Y1,
                        detection.Width,
                        detection.Height,
                        detection.Label,
                        group);
                    drawnThisFrame++;
                }

                _drawnCount = drawnThisFrame;
            });
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Detection error: {ex.Message}");
            Log("ERROR", $"Stack trace: {ex.StackTrace}");
        }
    }

    private void OnToggleHotkeyPressed(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(() =>
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
                    _renderer?.Clear();
                    _overlayWindow.Hide();
                }
            }
        });
    }

    private void Save()
    {
        if (_selectedGameProfile == null) return;

        try
        {
            // Update or create overlay settings
            _selectedGameProfile.Overlay ??= new OverlaySettings();
            _selectedGameProfile.Overlay.ConfidenceThreshold = ConfidenceThreshold;
            _selectedGameProfile.Overlay.ToggleHotkey = ToggleHotkey;
            _selectedGameProfile.Overlay.Groups = OverlayGroups.ToList();

            // Save directly to file
            var configPath = Path.Combine(_gameModelsDirectory, _selectedGameProfile.GameId, "game_config.json");
            var json = JsonSerializer.Serialize(_selectedGameProfile, JsonOptions);
            File.WriteAllText(configPath, json);

            MessageBox.Show("Settings saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close()
    {
        Cleanup();
        _window.Close();
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Cleans up all resources. Called when the window is closing.
    /// </summary>
    public void Cleanup()
    {
        if (IsOverlayRunning)
        {
            StopOverlay();
        }
    }

    private void OpenGameSettings()
    {
        if (_selectedGameProfile == null) return;

        var gameDirectory = Path.Combine(_gameModelsDirectory, _selectedGameProfile.GameId);
        var settingsWindow = new GameSettingsWindow(_selectedGameProfile, gameDirectory)
        {
            Owner = _window
        };

        if (settingsWindow.ShowDialog() == true)
        {
            // Save the updated profile
            Save();

            // Refresh the display name in the combo box
            OnPropertyChanged(nameof(SelectedGameProfile));
        }
    }

    private void UpdateCommandStates()
    {
        (AddGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveGroupUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveGroupDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StartOverlayCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenGameSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Simple relay command implementation.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
