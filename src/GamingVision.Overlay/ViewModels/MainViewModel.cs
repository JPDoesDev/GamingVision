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
        if (IsOverlayRunning)
        {
            StopOverlay();
        }
        else
        {
            await StartOverlayAsync();
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
                }
                catch { }
            }

            if (!await _detectionService.InitializeAsync(modelPath, useGpu))
            {
                MessageBox.Show($"Could not load model: {_selectedGameProfile.ModelFile}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _captureService.Dispose();
                _captureService = null;
                return;
            }

            // Create and show overlay window
            _overlayWindow = new OverlayWindow();
            _renderer = new OverlayRenderer(_overlayWindow.Canvas);
            _overlayWindow.Show();

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
        _hotkeyService?.Stop();
        _hotkeyService = null;

        if (_captureService != null)
        {
            _captureService.FrameCaptured -= OnFrameCaptured;
            _captureService.StopCapture();
            _captureService.Dispose();
            _captureService = null;
        }

        _detectionService?.Dispose();
        _detectionService = null;

        _overlayWindow?.Close();
        _overlayWindow = null;
        _renderer = null;

        IsOverlayRunning = false;
    }

    private async void OnFrameCaptured(object? sender, CapturedFrame frame)
    {
        if (_detectionService == null || _renderer == null || !_overlayVisible)
            return;

        try
        {
            // Use a very low base threshold, then filter by per-group confidence
            var detections = await _detectionService.DetectAsync(frame, 0.01f);
            if (detections == null) return; // Skipped frame

            _dispatcher.Invoke(() =>
            {
                _renderer.Clear();

                foreach (var detection in detections)
                {
                    // Find matching group for this label
                    var group = OverlayGroups.FirstOrDefault(g =>
                        g.Labels.Contains(detection.Label, StringComparer.OrdinalIgnoreCase));

                    // Check if detection meets the group's confidence threshold
                    if (group != null && detection.Confidence >= group.ConfidenceThreshold)
                    {
                        _renderer.DrawBox(
                            detection.X1,
                            detection.Y1,
                            detection.Width,
                            detection.Height,
                            detection.Label,
                            group);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Detection error: {ex.Message}");
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
        StopOverlay();
        _window.Close();
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
