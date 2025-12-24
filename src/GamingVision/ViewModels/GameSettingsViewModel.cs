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

    // Game profile settings
    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _modelFile = "";

    [ObservableProperty]
    private string _windowTitle = "";

    [ObservableProperty]
    private string _primaryLabels = "";

    [ObservableProperty]
    private string _secondaryLabels = "";

    [ObservableProperty]
    private string _tertiaryLabels = "";

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

    [ObservableProperty]
    private string _availableLabelsInfo = "";

    public GameSettingsViewModel(AppConfiguration appConfig, ConfigManager configManager)
    {
        _appConfig = appConfig;
        _configManager = configManager;

        LoadAvailableVoices();
        LoadGames();
    }

    /// <summary>
    /// Gets the available labels from the model's label file (.txt file alongside .onnx).
    /// </summary>
    public List<string> GetAvailableLabels()
    {
        if (_currentProfile == null || string.IsNullOrEmpty(_currentProfile.ModelFile))
            return [];

        try
        {
            // Get the model path and derive the labels file path
            var modelPath = _configManager.GetModelPath(_currentProfile.GameId, _currentProfile.ModelFile);
            var labelsPath = Path.ChangeExtension(modelPath, ".txt");

            if (File.Exists(labelsPath))
            {
                return File.ReadAllLines(labelsPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
            }

            // Fallback: try modelname_labels.txt
            var dir = Path.GetDirectoryName(modelPath) ?? ".";
            var name = Path.GetFileNameWithoutExtension(modelPath);
            labelsPath = Path.Combine(dir, $"{name}_labels.txt");

            if (File.Exists(labelsPath))
            {
                return File.ReadAllLines(labelsPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading labels file: {ex.Message}");
        }

        // Final fallback: return LabelPriority from config
        return _currentProfile?.LabelPriority ?? [];
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
        WindowTitle = _currentProfile.WindowTitle;
        PrimaryLabels = string.Join(", ", _currentProfile.PrimaryLabels);
        SecondaryLabels = string.Join(", ", _currentProfile.SecondaryLabels);
        TertiaryLabels = string.Join(", ", _currentProfile.TertiaryLabels);

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
    }

    private void SaveProfileSettings()
    {
        if (_currentProfile == null) return;

        // Game profile basics
        _currentProfile.DisplayName = DisplayName;
        _currentProfile.ModelFile = ModelFile;
        _currentProfile.WindowTitle = WindowTitle;
        _currentProfile.PrimaryLabels = ParseLabelList(PrimaryLabels);
        _currentProfile.SecondaryLabels = ParseLabelList(SecondaryLabels);
        _currentProfile.TertiaryLabels = ParseLabelList(TertiaryLabels);

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
    }

    private static List<string> ParseLabelList(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
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
            DisplayName = "New Game"
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

    /// <summary>
    /// Sets the window title from a selected window.
    /// </summary>
    public void SetWindowTitle(string title)
    {
        WindowTitle = title;
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
