using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VIGamingVision.Models;
using VIGamingVision.Utilities;

namespace VIGamingVision.ViewModels;

/// <summary>
/// ViewModel for the game settings window.
/// </summary>
public partial class GameSettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _config;
    private readonly ConfigManager _configManager;
    private GameProfile? _currentProfile;
    private string _currentProfileKey = "";

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
    private int _volume = 100;

    // Hotkey settings
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

    // Detection settings
    [ObservableProperty]
    private int _autoReadCooldown = 2000;

    [ObservableProperty]
    private float _confidenceThreshold = 0.5f;

    public GameSettingsViewModel(AppConfiguration config, ConfigManager configManager)
    {
        _config = config;
        _configManager = configManager;

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

        foreach (var (key, profile) in _config.Games)
        {
            Games.Add(new GameProfileItem
            {
                Key = key,
                DisplayName = profile.DisplayName
            });
        }

        SelectedGame = Games.FirstOrDefault(g => g.Key == _config.SelectedGame)
                       ?? Games.FirstOrDefault();
    }

    partial void OnSelectedGameChanged(GameProfileItem? value)
    {
        if (value == null) return;

        _config.Games.TryGetValue(value.Key, out _currentProfile);
        _currentProfileKey = value.Key;
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
        Volume = _currentProfile.Tts.Volume;

        // Hotkeys
        HotkeyReadPrimary = _currentProfile.Hotkeys.ReadPrimary;
        HotkeyReadSecondary = _currentProfile.Hotkeys.ReadSecondary;
        HotkeyStopReading = _currentProfile.Hotkeys.StopReading;
        HotkeyToggleDetection = _currentProfile.Hotkeys.ToggleDetection;
        HotkeyQuit = _currentProfile.Hotkeys.Quit;

        // Detection
        AutoReadCooldown = _currentProfile.Detection.AutoReadCooldown;
        ConfidenceThreshold = _currentProfile.Detection.ConfidenceThreshold;
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
        _currentProfile.Tts.Volume = Volume;

        // Hotkeys
        _currentProfile.Hotkeys.ReadPrimary = HotkeyReadPrimary;
        _currentProfile.Hotkeys.ReadSecondary = HotkeyReadSecondary;
        _currentProfile.Hotkeys.StopReading = HotkeyStopReading;
        _currentProfile.Hotkeys.ToggleDetection = HotkeyToggleDetection;
        _currentProfile.Hotkeys.Quit = HotkeyQuit;

        // Detection
        _currentProfile.Detection.AutoReadCooldown = AutoReadCooldown;
        _currentProfile.Detection.ConfidenceThreshold = ConfidenceThreshold;
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
        await _configManager.SaveAsync(_config);
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
    private void AddGame()
    {
        // Generate a unique key
        var key = $"game_{DateTime.Now:yyyyMMddHHmmss}";
        var newProfile = new GameProfile
        {
            DisplayName = "New Game"
        };

        _config.Games[key] = newProfile;

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

        // Remove from config and list
        _config.Games.Remove(keyToDelete);
        Games.Remove(gameToDelete);

        // Update selected game in config
        if (newSelection != null)
        {
            _config.SelectedGame = newSelection.Key;
        }

        CanDeleteGame = Games.Count > 1;

        await _configManager.SaveAsync(_config);
    }

    [RelayCommand]
    private void BrowseForModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select ONNX Model File",
            Filter = "ONNX Models (*.onnx)|*.onnx|All Files (*.*)|*.*",
            InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models")
        };

        if (dialog.ShowDialog() == true)
        {
            ModelFile = dialog.FileName;
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
