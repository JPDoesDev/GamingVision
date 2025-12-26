namespace GamingVision.Services.Hotkeys;

/// <summary>
/// Interface for global hotkey services.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Gets whether the hotkey service is initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the hotkey service with a window handle for message processing.
    /// </summary>
    /// <param name="windowHandle">The window handle to receive hotkey messages.</param>
    /// <returns>True if initialization succeeded.</returns>
    bool Initialize(IntPtr windowHandle);

    /// <summary>
    /// Registers a hotkey.
    /// </summary>
    /// <param name="id">Unique identifier for the hotkey.</param>
    /// <param name="hotkeyString">Hotkey string (e.g., "Alt+1", "Ctrl+Shift+R").</param>
    /// <returns>True if registration succeeded.</returns>
    bool RegisterHotkey(HotkeyId id, string hotkeyString);

    /// <summary>
    /// Unregisters a hotkey.
    /// </summary>
    /// <param name="id">The hotkey identifier to unregister.</param>
    /// <returns>True if unregistration succeeded.</returns>
    bool UnregisterHotkey(HotkeyId id);

    /// <summary>
    /// Unregisters all registered hotkeys.
    /// </summary>
    void UnregisterAll();

    /// <summary>
    /// Processes a Windows message to check for hotkey activation.
    /// </summary>
    /// <param name="msg">The message ID.</param>
    /// <param name="wParam">The wParam containing the hotkey ID.</param>
    /// <returns>The hotkey ID if this was a hotkey message, null otherwise.</returns>
    HotkeyId? ProcessMessage(int msg, IntPtr wParam);

    /// <summary>
    /// Event raised when a hotkey is pressed.
    /// </summary>
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
}

/// <summary>
/// Identifiers for application hotkeys.
/// </summary>
public enum HotkeyId
{
    ReadPrimary = 1,
    ReadSecondary = 2,
    ReadTertiary = 3,
    StopReading = 4,
    ToggleDetection = 5,
    Quit = 6,
    CaptureTraining = 7
}

/// <summary>
/// Event arguments for hotkey pressed events.
/// </summary>
public class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyId HotkeyId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a parsed hotkey combination.
/// </summary>
public class HotkeyCombination
{
    public uint Modifiers { get; init; }
    public uint VirtualKey { get; init; }
    public string OriginalString { get; init; } = string.Empty;

    public bool IsValid => VirtualKey != 0;
}
