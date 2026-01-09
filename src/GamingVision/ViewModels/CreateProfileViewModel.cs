using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingVision.Models;
using GamingVision.Services.ScreenCapture;
using GamingVision.Services.Training;
using GamingVision.Utilities;

namespace GamingVision.ViewModels;

/// <summary>
/// ViewModel for the Create Profile dialog.
/// </summary>
public partial class CreateProfileViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _gameId = string.Empty;

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private bool _useWindowCapture = true;

    [ObservableProperty]
    private bool _useFullscreenCapture;

    [ObservableProperty]
    private int _monitorIndex;

    [ObservableProperty]
    private ObservableCollection<string> _monitors = [];

    [ObservableProperty]
    private ObservableCollection<string> _availableWindows = [];

    [ObservableProperty]
    private bool _canCreate;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// The created game profile, set after successful creation.
    /// </summary>
    public GameProfile? CreatedProfile { get; private set; }

    public CreateProfileViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        LoadMonitors();
        RefreshWindows();
    }

    private void LoadMonitors()
    {
        var monitorList = ScreenCaptureManager.GetAvailableMonitors();
        Monitors.Clear();
        foreach (var monitor in monitorList)
        {
            Monitors.Add($"Monitor {monitor.Index}: {monitor.Bounds.Width}x{monitor.Bounds.Height}{(monitor.IsPrimary ? " (Primary)" : "")}");
        }

        if (Monitors.Count == 0)
        {
            Monitors.Add("Monitor 0: Primary");
        }
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        var currentSelection = WindowTitle;
        AvailableWindows.Clear();

        // Add empty option
        AvailableWindows.Add("");

        // Get visible windows
        foreach (var window in GetVisibleWindows())
        {
            if (!string.IsNullOrWhiteSpace(window))
            {
                AvailableWindows.Add(window);
            }
        }

        // Restore selection if it still exists
        if (!string.IsNullOrEmpty(currentSelection) && AvailableWindows.Contains(currentSelection))
        {
            WindowTitle = currentSelection;
        }
        else if (!string.IsNullOrEmpty(currentSelection))
        {
            // Window not found but had a value - add it so user sees what was configured
            AvailableWindows.Add(currentSelection);
            WindowTitle = currentSelection;
        }
    }

    private List<string> GetVisibleWindows()
    {
        var windows = new List<string>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            // Filter out common system windows
            if (!string.IsNullOrWhiteSpace(title) &&
                title != "Program Manager" &&
                title != "Windows Input Experience" &&
                !title.StartsWith("Microsoft Text Input") &&
                !title.Contains("GamingVision"))
            {
                windows.Add(title);
            }

            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(w => w).ToList();
    }

    #region Win32 Interop

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    #endregion

    partial void OnDisplayNameChanged(string value)
    {
        // Auto-generate game ID from display name
        if (!string.IsNullOrWhiteSpace(value))
        {
            // Convert to lowercase, replace spaces with underscores, remove special chars
            var id = value.ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9\s]", "");
            id = Regex.Replace(id, @"\s+", "_");
            id = id.Trim('_');
            GameId = id;
        }

        ValidateInput();
    }

    partial void OnGameIdChanged(string value)
    {
        ValidateInput();
    }

    partial void OnWindowTitleChanged(string value)
    {
        ValidateInput();
    }

    private void ValidateInput()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            CanCreate = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(GameId))
        {
            CanCreate = false;
            return;
        }

        // Check if game ID is valid (only alphanumeric and underscores)
        if (!Regex.IsMatch(GameId, @"^[a-z0-9_]+$"))
        {
            ErrorMessage = "Game ID can only contain lowercase letters, numbers, and underscores.";
            CanCreate = false;
            return;
        }

        // Check if game ID already exists
        var existingProfile = _configManager.GetGameProfile(GameId);
        if (existingProfile != null)
        {
            ErrorMessage = $"A game profile with ID '{GameId}' already exists.";
            CanCreate = false;
            return;
        }

        CanCreate = true;
    }

    public async Task<bool> CreateProfileAsync()
    {
        try
        {
            // Create the game profile
            var profile = new GameProfile
            {
                GameId = GameId,
                DisplayName = DisplayName,
                ModelFile = $"{GameId}_model.onnx",
                WindowTitle = WindowTitle,
                PrimaryLabels = [],
                SecondaryLabels = [],
                TertiaryLabels = [],
                LabelPriority = [],
                Labels = [],
                Hotkeys = new HotkeySettings(),
                Capture = new CaptureSettings
                {
                    Method = UseWindowCapture ? "window" : "fullscreen",
                    MonitorIndex = MonitorIndex
                },
                Tts = new TtsSettings(),
                Detection = new DetectionSettings(),
                Overlay = new OverlaySettings(),
                Training = new TrainingSettings
                {
                    Enabled = false, // Default to disabled, user enables when ready
                    DataPath = string.Empty // Will use default path
                }
            };

            // Save the profile (this creates the GameModels/{gameId} directory and game_config.json)
            await _configManager.SaveGameProfileAsync(profile);

            // Verify the config file was created
            var configPath = Path.Combine(_configManager.GameModelsDirectory, GameId, "game_config.json");
            if (!File.Exists(configPath))
            {
                throw new Exception($"Config file was not created at: {configPath}");
            }

            Logger.Log($"Config file created at: {configPath}");

            // Create the training data directories
            var trainingDataRoot = TrainingDataManager.GetDefaultTrainingDataRoot();
            var trainingManager = new TrainingDataManager(trainingDataRoot, GameId);
            trainingManager.Initialize();

            CreatedProfile = profile;
            Logger.Log($"Created new game profile: {DisplayName} ({GameId})");

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Logger.Error("Failed to create game profile", ex);
            return false;
        }
    }
}
