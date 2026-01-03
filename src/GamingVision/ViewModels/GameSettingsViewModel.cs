using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision.ViewModels;

/// <summary>
/// ViewModel for the game settings window.
/// </summary>
public partial class GameSettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _appConfig;
    private readonly ConfigManager _configManager;
    private GameProfile? _currentProfile;

    [ObservableProperty]
    private ObservableCollection<GameProfileItem> _games = [];

    [ObservableProperty]
    private GameProfileItem? _selectedGame;

    [ObservableProperty]
    private ObservableCollection<string> _availableVoices = [];

    [ObservableProperty]
    private ObservableCollection<string> _captureMethodOptions = ["window", "fullscreen"];

    [ObservableProperty]
    private ObservableCollection<string> _availableWindows = [];

    [ObservableProperty]
    private string _selectedWindow = "";

    // Game profile settings
    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _modelFile = "";

    // Label lists (internal storage)
    private List<string> _primaryLabelsList = [];
    private List<string> _secondaryLabelsList = [];
    private List<string> _tertiaryLabelsList = [];

    // Display properties for labels summary
    [ObservableProperty]
    private string _primaryLabelsDisplay = "";

    [ObservableProperty]
    private string _secondaryLabelsDisplay = "";

    [ObservableProperty]
    private string _tertiaryLabelsDisplay = "";

    // Waypoint settings
    private WaypointSettings _waypointSettings = new();

    [ObservableProperty]
    private string _waypointDisplay = "(disabled)";

    [ObservableProperty]
    private bool _canDeleteGame;

    // Capture settings
    [ObservableProperty]
    private string _captureMethod = "window";

    [ObservableProperty]
    private int _monitorIndex;

    // TTS settings
    [ObservableProperty]
    private string _ttsEngine = "sapi";

    [ObservableProperty]
    private string _primaryVoice = "";

    [ObservableProperty]
    private int _primaryRate;

    [ObservableProperty]
    private string _secondaryVoice = "";

    [ObservableProperty]
    private int _secondaryRate;

    [ObservableProperty]
    private string _tertiaryVoice = "";

    [ObservableProperty]
    private int _tertiaryRate;

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private bool _primaryDirectionalAudio;

    [ObservableProperty]
    private bool _secondaryDirectionalAudio;

    [ObservableProperty]
    private bool _tertiaryDirectionalAudio;

    // Hotkey settings
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

    // Detection settings
    [ObservableProperty]
    private bool _autoReadEnabled = false;

    [ObservableProperty]
    private int _autoReadCooldown = 2000;

    [ObservableProperty]
    private float _confidenceThreshold = 0.3f;

    [ObservableProperty]
    private float _autoReadConfidenceThreshold = 0.6f;

    [ObservableProperty]
    private bool _readPrimaryLabelAloud = true;

    [ObservableProperty]
    private bool _readSecondaryLabelAloud = false;

    [ObservableProperty]
    private bool _readTertiaryLabelAloud = false;

    // Crosshair settings
    [ObservableProperty]
    private int _crosshairOffsetX;

    [ObservableProperty]
    private int _crosshairOffsetY;

    [ObservableProperty]
    private string _crosshairShape = "Circle";

    [ObservableProperty]
    private int _crosshairSize = 20;

    [ObservableProperty]
    private int _crosshairColorRed = 255;

    [ObservableProperty]
    private int _crosshairColorGreen = 255;

    [ObservableProperty]
    private int _crosshairColorBlue = 255;

    [ObservableProperty]
    private int _crosshairThickness = 2;

    [ObservableProperty]
    private int _crosshairOutlineRed;

    [ObservableProperty]
    private int _crosshairOutlineGreen;

    [ObservableProperty]
    private int _crosshairOutlineBlue;

    [ObservableProperty]
    private int _crosshairOutlineThickness = 1;

    /// <summary>
    /// Gets the crosshair color as a hex string for preview binding.
    /// </summary>
    public string CrosshairColor => $"#{CrosshairColorRed:X2}{CrosshairColorGreen:X2}{CrosshairColorBlue:X2}";

    /// <summary>
    /// Gets the crosshair outline color as a hex string for preview binding.
    /// </summary>
    public string CrosshairOutlineColor => $"#{CrosshairOutlineRed:X2}{CrosshairOutlineGreen:X2}{CrosshairOutlineBlue:X2}";

    // Notify color preview when RGB values change and trigger live crosshair update
    partial void OnCrosshairColorRedChanged(int value) { OnPropertyChanged(nameof(CrosshairColor)); NotifyCrosshairPreview(); }
    partial void OnCrosshairColorGreenChanged(int value) { OnPropertyChanged(nameof(CrosshairColor)); NotifyCrosshairPreview(); }
    partial void OnCrosshairColorBlueChanged(int value) { OnPropertyChanged(nameof(CrosshairColor)); NotifyCrosshairPreview(); }
    partial void OnCrosshairOutlineRedChanged(int value) { OnPropertyChanged(nameof(CrosshairOutlineColor)); NotifyCrosshairPreview(); }
    partial void OnCrosshairOutlineGreenChanged(int value) { OnPropertyChanged(nameof(CrosshairOutlineColor)); NotifyCrosshairPreview(); }
    partial void OnCrosshairOutlineBlueChanged(int value) { OnPropertyChanged(nameof(CrosshairOutlineColor)); NotifyCrosshairPreview(); }

    // Trigger live crosshair update for other properties
    partial void OnCrosshairOffsetXChanged(int value) => NotifyCrosshairPreview();
    partial void OnCrosshairOffsetYChanged(int value) => NotifyCrosshairPreview();
    partial void OnCrosshairShapeChanged(string value) => NotifyCrosshairPreview();
    partial void OnCrosshairSizeChanged(int value) => NotifyCrosshairPreview();
    partial void OnCrosshairThicknessChanged(int value) => NotifyCrosshairPreview();
    partial void OnCrosshairOutlineThicknessChanged(int value) => NotifyCrosshairPreview();

    /// <summary>
    /// Notifies the crosshair preview callback with current settings.
    /// </summary>
    private void NotifyCrosshairPreview()
    {
        if (_crosshairPreviewCallback == null) return;

        var settings = new CrosshairSettings
        {
            OffsetX = CrosshairOffsetX,
            OffsetY = CrosshairOffsetY,
            Shape = CrosshairShape,
            Size = CrosshairSize,
            Color = CrosshairColor,
            OutlineColor = CrosshairOutlineColor,
            Thickness = CrosshairThickness,
            OutlineThickness = CrosshairOutlineThickness
        };

        _crosshairPreviewCallback(settings);
    }

    private readonly Action<CrosshairSettings>? _crosshairPreviewCallback;

    public GameSettingsViewModel(AppConfiguration appConfig, ConfigManager configManager, Action<CrosshairSettings>? crosshairPreviewCallback = null)
    {
        _appConfig = appConfig;
        _configManager = configManager;
        _crosshairPreviewCallback = crosshairPreviewCallback;

        LoadAvailableVoices();
        LoadGames();
    }

    private void LoadAvailableVoices()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            foreach (var voice in synth.GetInstalledVoices())
            {
                if (voice.Enabled)
                {
                    AvailableVoices.Add(voice.VoiceInfo.Name);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading voices: {ex.Message}");
            AvailableVoices.Add("Microsoft David");
        }
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

        SelectedGame = Games.FirstOrDefault(g => g.Key == _appConfig.SelectedGame)
                       ?? Games.FirstOrDefault();
    }

    partial void OnSelectedGameChanged(GameProfileItem? value)
    {
        if (value == null) return;

        _currentProfile = _configManager.GetGameProfile(value.Key);
        LoadProfileSettings();
    }

    private void LoadProfileSettings()
    {
        if (_currentProfile == null) return;

        // Game profile basics
        DisplayName = _currentProfile.DisplayName;
        ModelFile = _currentProfile.ModelFile;

        // Set window title BEFORE refreshing so it can be preserved if window isn't open
        SelectedWindow = _currentProfile.WindowTitle;
        RefreshAvailableWindows();

        // Load label lists
        _primaryLabelsList = [.. _currentProfile.PrimaryLabels];
        _secondaryLabelsList = [.. _currentProfile.SecondaryLabels];
        _tertiaryLabelsList = [.. _currentProfile.TertiaryLabels];
        UpdateLabelDisplays();

        // Load waypoint settings
        _waypointSettings = _currentProfile.Waypoint?.Clone() ?? new WaypointSettings();
        UpdateWaypointDisplay();

        // Can only delete if there's more than one game
        CanDeleteGame = Games.Count > 1;

        // Capture
        CaptureMethod = _currentProfile.Capture.Method;
        MonitorIndex = _currentProfile.Capture.MonitorIndex;

        // TTS
        TtsEngine = _currentProfile.Tts.Engine;
        PrimaryVoice = _currentProfile.Tts.PrimaryVoice;
        PrimaryRate = _currentProfile.Tts.PrimaryRate;
        SecondaryVoice = _currentProfile.Tts.SecondaryVoice;
        SecondaryRate = _currentProfile.Tts.SecondaryRate;
        TertiaryVoice = _currentProfile.Tts.TertiaryVoice;
        TertiaryRate = _currentProfile.Tts.TertiaryRate;
        Volume = _currentProfile.Tts.Volume;
        PrimaryDirectionalAudio = _currentProfile.Tts.PrimaryDirectionalAudio;
        SecondaryDirectionalAudio = _currentProfile.Tts.SecondaryDirectionalAudio;
        TertiaryDirectionalAudio = _currentProfile.Tts.TertiaryDirectionalAudio;

        // Hotkeys
        HotkeyReadPrimary = _currentProfile.Hotkeys.ReadPrimary;
        HotkeyReadSecondary = _currentProfile.Hotkeys.ReadSecondary;
        HotkeyReadTertiary = _currentProfile.Hotkeys.ReadTertiary;
        HotkeyStopReading = _currentProfile.Hotkeys.StopReading;
        HotkeyToggleDetection = _currentProfile.Hotkeys.ToggleDetection;
        HotkeyQuit = _currentProfile.Hotkeys.Quit;

        // Detection
        AutoReadEnabled = _currentProfile.Detection.AutoReadEnabled;
        AutoReadCooldown = _currentProfile.Detection.AutoReadCooldown;
        ConfidenceThreshold = _currentProfile.Detection.ConfidenceThreshold;
        AutoReadConfidenceThreshold = _currentProfile.Detection.AutoReadConfidenceThreshold;
        ReadPrimaryLabelAloud = _currentProfile.Detection.ReadPrimaryLabelAloud;
        ReadSecondaryLabelAloud = _currentProfile.Detection.ReadSecondaryLabelAloud;
        ReadTertiaryLabelAloud = _currentProfile.Detection.ReadTertiaryLabelAloud;

        // Crosshair
        var crosshair = _currentProfile.Crosshair ?? new CrosshairSettings();
        CrosshairOffsetX = crosshair.OffsetX;
        CrosshairOffsetY = crosshair.OffsetY;
        CrosshairShape = crosshair.Shape;
        CrosshairSize = crosshair.Size;
        CrosshairThickness = crosshair.Thickness;
        CrosshairOutlineThickness = crosshair.OutlineThickness;

        // Parse crosshair color
        ParseHexColor(crosshair.Color, out int cr, out int cg, out int cb);
        CrosshairColorRed = cr;
        CrosshairColorGreen = cg;
        CrosshairColorBlue = cb;

        // Parse outline color
        ParseHexColor(crosshair.OutlineColor, out int or, out int og, out int ob);
        CrosshairOutlineRed = or;
        CrosshairOutlineGreen = og;
        CrosshairOutlineBlue = ob;
    }

    private static void ParseHexColor(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex)) return;

        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                r = Convert.ToInt32(hex.Substring(0, 2), 16);
                g = Convert.ToInt32(hex.Substring(2, 2), 16);
                b = Convert.ToInt32(hex.Substring(4, 2), 16);
            }
        }
        catch { /* ignore parse errors */ }
    }

    private void SaveProfileSettings()
    {
        if (_currentProfile == null) return;

        // Game profile basics
        _currentProfile.DisplayName = DisplayName;
        _currentProfile.ModelFile = ModelFile;
        _currentProfile.WindowTitle = SelectedWindow;
        _currentProfile.PrimaryLabels = [.. _primaryLabelsList];
        _currentProfile.SecondaryLabels = [.. _secondaryLabelsList];
        _currentProfile.TertiaryLabels = [.. _tertiaryLabelsList];

        // Waypoint settings
        _currentProfile.Waypoint = _waypointSettings.Clone();

        // Update display name in list
        if (SelectedGame != null)
        {
            SelectedGame.DisplayName = DisplayName;
        }

        // Capture
        _currentProfile.Capture.Method = CaptureMethod;
        _currentProfile.Capture.MonitorIndex = MonitorIndex;

        // TTS
        _currentProfile.Tts.Engine = TtsEngine;
        _currentProfile.Tts.PrimaryVoice = PrimaryVoice;
        _currentProfile.Tts.PrimaryRate = PrimaryRate;
        _currentProfile.Tts.SecondaryVoice = SecondaryVoice;
        _currentProfile.Tts.SecondaryRate = SecondaryRate;
        _currentProfile.Tts.TertiaryVoice = TertiaryVoice;
        _currentProfile.Tts.TertiaryRate = TertiaryRate;
        _currentProfile.Tts.Volume = Volume;
        _currentProfile.Tts.PrimaryDirectionalAudio = PrimaryDirectionalAudio;
        _currentProfile.Tts.SecondaryDirectionalAudio = SecondaryDirectionalAudio;
        _currentProfile.Tts.TertiaryDirectionalAudio = TertiaryDirectionalAudio;

        // Hotkeys
        _currentProfile.Hotkeys.ReadPrimary = HotkeyReadPrimary;
        _currentProfile.Hotkeys.ReadSecondary = HotkeyReadSecondary;
        _currentProfile.Hotkeys.ReadTertiary = HotkeyReadTertiary;
        _currentProfile.Hotkeys.StopReading = HotkeyStopReading;
        _currentProfile.Hotkeys.ToggleDetection = HotkeyToggleDetection;
        _currentProfile.Hotkeys.Quit = HotkeyQuit;

        // Detection
        _currentProfile.Detection.AutoReadEnabled = AutoReadEnabled;
        _currentProfile.Detection.AutoReadCooldown = AutoReadCooldown;
        _currentProfile.Detection.ConfidenceThreshold = ConfidenceThreshold;
        _currentProfile.Detection.AutoReadConfidenceThreshold = AutoReadConfidenceThreshold;
        _currentProfile.Detection.ReadPrimaryLabelAloud = ReadPrimaryLabelAloud;
        _currentProfile.Detection.ReadSecondaryLabelAloud = ReadSecondaryLabelAloud;
        _currentProfile.Detection.ReadTertiaryLabelAloud = ReadTertiaryLabelAloud;

        // Crosshair
        _currentProfile.Crosshair ??= new CrosshairSettings();
        _currentProfile.Crosshair.OffsetX = CrosshairOffsetX;
        _currentProfile.Crosshair.OffsetY = CrosshairOffsetY;
        _currentProfile.Crosshair.Shape = CrosshairShape;
        _currentProfile.Crosshair.Size = CrosshairSize;
        _currentProfile.Crosshair.Thickness = CrosshairThickness;
        _currentProfile.Crosshair.OutlineThickness = CrosshairOutlineThickness;
        _currentProfile.Crosshair.Color = CrosshairColor;
        _currentProfile.Crosshair.OutlineColor = CrosshairOutlineColor;
    }

    private void UpdateLabelDisplays()
    {
        PrimaryLabelsDisplay = _primaryLabelsList.Count > 0
            ? string.Join(", ", _primaryLabelsList)
            : "(none configured)";
        SecondaryLabelsDisplay = _secondaryLabelsList.Count > 0
            ? string.Join(", ", _secondaryLabelsList)
            : "(none configured)";
        TertiaryLabelsDisplay = _tertiaryLabelsList.Count > 0
            ? string.Join(", ", _tertiaryLabelsList)
            : "(none configured)";
    }

    private void UpdateWaypointDisplay()
    {
        if (_waypointSettings.Enabled && !string.IsNullOrEmpty(_waypointSettings.Label))
        {
            WaypointDisplay = $"{_waypointSettings.Label} ({_waypointSettings.ReadIntervalSeconds:F1}s)";
        }
        else
        {
            WaypointDisplay = "(disabled)";
        }
    }

    /// <summary>
    /// Opens the waypoint configuration window.
    /// </summary>
    public void OpenWaypointConfiguration(System.Windows.Window owner)
    {
        if (_currentProfile == null) return;

        var viewModel = new WaypointConfigViewModel(_currentProfile.Labels, _waypointSettings);
        var window = new Views.WaypointConfigWindow(viewModel) { Owner = owner };

        if (window.ShowDialog() == true)
        {
            _waypointSettings = viewModel.GetSettings();
            UpdateWaypointDisplay();
        }
    }

    /// <summary>
    /// Opens the label configuration window for a specific tier.
    /// </summary>
    public void OpenLabelConfiguration(string tierName, System.Windows.Window owner)
    {
        if (_currentProfile == null) return;

        // Determine which label list to use
        List<string> currentLabels = tierName switch
        {
            "Primary" => _primaryLabelsList,
            "Secondary" => _secondaryLabelsList,
            "Tertiary" => _tertiaryLabelsList,
            _ => []
        };

        bool readLabelAloud = tierName switch
        {
            "Primary" => ReadPrimaryLabelAloud,
            "Secondary" => ReadSecondaryLabelAloud,
            "Tertiary" => ReadTertiaryLabelAloud,
            _ => false
        };

        // Only pass autoReadEnabled for Primary tier
        bool? autoReadEnabled = tierName == "Primary" ? AutoReadEnabled : null;

        var viewModel = new LabelConfigurationViewModel(
            tierName,
            _currentProfile.Labels,
            currentLabels,
            readLabelAloud,
            autoReadEnabled);

        var window = new Views.LabelConfigurationWindow(viewModel)
        {
            Owner = owner
        };

        if (window.ShowDialog() == true)
        {
            // Update the label list based on tier
            var newLabels = viewModel.GetSelectedLabelNames();
            switch (tierName)
            {
                case "Primary":
                    _primaryLabelsList = newLabels;
                    ReadPrimaryLabelAloud = viewModel.ReadLabelAloud;
                    AutoReadEnabled = viewModel.AutoReadEnabled;
                    break;
                case "Secondary":
                    _secondaryLabelsList = newLabels;
                    ReadSecondaryLabelAloud = viewModel.ReadLabelAloud;
                    break;
                case "Tertiary":
                    _tertiaryLabelsList = newLabels;
                    ReadTertiaryLabelAloud = viewModel.ReadLabelAloud;
                    break;
            }
            UpdateLabelDisplays();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveProfileSettings();

        // Save game profile to its own file
        if (_currentProfile != null)
        {
            await _configManager.SaveGameProfileAsync(_currentProfile);
        }

        // Update selected game in app settings if changed
        if (SelectedGame != null && _appConfig.SelectedGame != SelectedGame.Key)
        {
            _appConfig.SelectedGame = SelectedGame.Key;
            await _configManager.SaveAppSettingsAsync(_appConfig);
        }
    }

    /// <summary>
    /// Saves settings and returns true to indicate dialog should close.
    /// </summary>
    public async Task<bool> SaveAndCloseAsync()
    {
        await SaveAsync();
        return true;
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        // Generate a unique key
        var key = $"game_{DateTime.Now:yyyyMMddHHmmss}";
        var newProfile = new GameProfile
        {
            GameId = key,
            DisplayName = "New Game",
            Labels =
            [
                new LabelDefinition { Name = "example_label", Description = "Example label - replace with your own" }
            ]
        };

        // Save to file
        await _configManager.SaveGameProfileAsync(newProfile);

        var item = new GameProfileItem
        {
            Key = key,
            DisplayName = newProfile.DisplayName
        };
        Games.Add(item);
        SelectedGame = item;

        CanDeleteGame = Games.Count > 1;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGame))]
    private async Task DeleteGameAsync()
    {
        if (SelectedGame == null || Games.Count <= 1) return;

        var gameToDelete = SelectedGame;
        var keyToDelete = gameToDelete.Key;

        // Select another game first
        var newSelection = Games.FirstOrDefault(g => g.Key != keyToDelete);
        SelectedGame = newSelection;

        // Remove from config manager and list
        await _configManager.DeleteGameProfileAsync(keyToDelete);
        Games.Remove(gameToDelete);

        // Update selected game in app config
        if (newSelection != null)
        {
            _appConfig.SelectedGame = newSelection.Key;
            await _configManager.SaveAppSettingsAsync(_appConfig);
        }

        CanDeleteGame = Games.Count > 1;
    }

    [RelayCommand]
    private void BrowseForModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select ONNX Model File",
            Filter = "ONNX Models (*.onnx)|*.onnx|All Files (*.*)|*.*",
            InitialDirectory = _configManager.GameModelsDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            // Store just the filename, as models should be in the game's folder
            ModelFile = Path.GetFileName(dialog.FileName);
        }
    }

    /// <summary>
    /// Refreshes the list of available windows for the dropdown.
    /// </summary>
    [RelayCommand]
    private void RefreshAvailableWindows()
    {
        var currentSelection = SelectedWindow;
        AvailableWindows.Clear();

        // Add empty option for no window selected
        AvailableWindows.Add("");

        foreach (var window in GetAvailableWindows())
        {
            AvailableWindows.Add(window.Title);
        }

        // Restore selection if it still exists, otherwise keep empty
        if (!string.IsNullOrEmpty(currentSelection) && AvailableWindows.Contains(currentSelection))
        {
            SelectedWindow = currentSelection;
        }
        else if (!string.IsNullOrEmpty(currentSelection))
        {
            // Window not found but had a value - add it anyway so user can see what was configured
            AvailableWindows.Add(currentSelection);
            SelectedWindow = currentSelection;
        }
    }

    /// <summary>
    /// Gets available windows for window picker.
    /// </summary>
    public List<WindowInfo> GetAvailableWindows()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var builder = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, builder, builder.Capacity);
            var title = builder.ToString();

            if (!string.IsNullOrWhiteSpace(title))
            {
                windows.Add(new WindowInfo { Handle = hWnd, Title = title });
            }
            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(w => w.Title).ToList();
    }

    #region Win32 API for Window Enumeration

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    #endregion
}

/// <summary>
/// Information about a window for the window picker.
/// </summary>
public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
}
