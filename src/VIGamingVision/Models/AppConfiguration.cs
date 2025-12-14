namespace VIGamingVision.Models;

/// <summary>
/// Root configuration object containing all application and game settings.
/// </summary>
public class AppConfiguration
{
    /// <summary>
    /// Configuration file version for migration purposes.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Application-wide settings.
    /// </summary>
    public ApplicationSettings Application { get; set; } = new();

    /// <summary>
    /// Currently selected game profile key.
    /// </summary>
    public string SelectedGame { get; set; } = string.Empty;

    /// <summary>
    /// Dictionary of game profiles keyed by game identifier.
    /// </summary>
    public Dictionary<string, GameProfile> Games { get; set; } = [];

    /// <summary>
    /// Gets the currently selected game profile, or null if not found.
    /// </summary>
    public GameProfile? GetSelectedGameProfile()
    {
        if (string.IsNullOrEmpty(SelectedGame))
            return null;

        return Games.TryGetValue(SelectedGame, out var profile) ? profile : null;
    }

    /// <summary>
    /// Creates a default configuration with sample game profiles.
    /// </summary>
    public static AppConfiguration CreateDefault()
    {
        var config = new AppConfiguration
        {
            Version = "1.0",
            SelectedGame = "nomanssky",
            Application = new ApplicationSettings
            {
                UseDirectML = true,
                AutoStartDetection = false,
                MinimizeToTray = false,
                StartMinimized = false,
                EnableLogging = false,
                LogFilePath = "logs/vigamingvision.log",
                CheckForUpdates = true,
                Accessibility = new AccessibilitySettings
                {
                    HighContrast = false,
                    LargeText = false
                }
            }
        };

        // No Man's Sky profile
        config.Games["nomanssky"] = new GameProfile
        {
            DisplayName = "No Man's Sky",
            ModelFile = "models/nomanssky.onnx",
            WindowTitle = "No Man's Sky",
            PrimaryLabels = ["title", "item", "resource"],
            SecondaryLabels = ["info", "quest", "waypoint"],
            LabelPriority = ["title", "item", "resource", "info", "quest", "waypoint"],
            Hotkeys = new HotkeySettings
            {
                ReadPrimary = "Alt+1",
                ReadSecondary = "Alt+2",
                StopReading = "Alt+3",
                ToggleDetection = "Alt+4",
                Quit = "Alt+Q"
            },
            Capture = new CaptureSettings
            {
                Method = "window",
                MonitorIndex = 0
            },
            Tts = new TtsSettings
            {
                Engine = "sapi",
                PrimaryVoice = "",
                PrimaryRate = 3,
                SecondaryVoice = "",
                SecondaryRate = 0,
                Volume = 100
            },
            Detection = new DetectionSettings
            {
                AutoReadCooldown = 2000,
                ConfidenceThreshold = 0.5f
            }
        };

        return config;
    }
}
