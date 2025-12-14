using System.Diagnostics;
using System.Windows.Input;
using VIGamingVision.Native;

namespace VIGamingVision.Services.Hotkeys;

/// <summary>
/// Global hotkey service using Windows RegisterHotKey API.
/// </summary>
public class GlobalHotkeyService : IHotkeyService
{
    private IntPtr _windowHandle;
    private readonly Dictionary<HotkeyId, HotkeyCombination> _registeredHotkeys = new();
    private bool _disposed;

    public bool IsInitialized => _windowHandle != IntPtr.Zero;

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>
    /// Initializes the hotkey service with a window handle.
    /// </summary>
    public bool Initialize(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return false;

        _windowHandle = windowHandle;
        return true;
    }

    /// <summary>
    /// Registers a hotkey from a string like "Alt+1" or "Ctrl+Shift+R".
    /// </summary>
    public bool RegisterHotkey(HotkeyId id, string hotkeyString)
    {
        if (!IsInitialized)
            return false;

        // Unregister existing hotkey with this ID
        UnregisterHotkey(id);

        var combination = ParseHotkeyString(hotkeyString);
        if (!combination.IsValid)
        {
            Debug.WriteLine($"Invalid hotkey string: {hotkeyString}");
            return false;
        }

        // Register with Windows - use MOD_NOREPEAT to prevent repeated firing when held
        bool success = User32.RegisterHotKey(
            _windowHandle,
            (int)id,
            combination.Modifiers | User32.MOD_NOREPEAT,
            combination.VirtualKey);

        if (success)
        {
            _registeredHotkeys[id] = combination;
            Debug.WriteLine($"Registered hotkey {id}: {hotkeyString}");
        }
        else
        {
            Debug.WriteLine($"Failed to register hotkey {id}: {hotkeyString}");
        }

        return success;
    }

    /// <summary>
    /// Unregisters a hotkey.
    /// </summary>
    public bool UnregisterHotkey(HotkeyId id)
    {
        if (!IsInitialized)
            return false;

        if (_registeredHotkeys.ContainsKey(id))
        {
            bool success = User32.UnregisterHotKey(_windowHandle, (int)id);
            if (success)
            {
                _registeredHotkeys.Remove(id);
                Debug.WriteLine($"Unregistered hotkey {id}");
            }
            return success;
        }

        return true;
    }

    /// <summary>
    /// Unregisters all hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var id in _registeredHotkeys.Keys.ToList())
        {
            UnregisterHotkey(id);
        }
    }

    /// <summary>
    /// Processes a Windows message to check for hotkey activation.
    /// </summary>
    public HotkeyId? ProcessMessage(int msg, IntPtr wParam)
    {
        if (msg != User32.WM_HOTKEY)
            return null;

        var hotkeyId = (HotkeyId)wParam.ToInt32();

        if (_registeredHotkeys.ContainsKey(hotkeyId))
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs
            {
                HotkeyId = hotkeyId
            });
            return hotkeyId;
        }

        return null;
    }

    /// <summary>
    /// Parses a hotkey string like "Alt+1" or "Ctrl+Shift+R" into modifiers and virtual key.
    /// </summary>
    private static HotkeyCombination ParseHotkeyString(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            return new HotkeyCombination { OriginalString = hotkeyString };
        }

        uint modifiers = 0;
        uint virtualKey = 0;

        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();

            switch (upperPart)
            {
                case "ALT":
                    modifiers |= User32.MOD_ALT;
                    break;
                case "CTRL":
                case "CONTROL":
                    modifiers |= User32.MOD_CONTROL;
                    break;
                case "SHIFT":
                    modifiers |= User32.MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= User32.MOD_WIN;
                    break;
                default:
                    // This should be the key
                    virtualKey = GetVirtualKey(upperPart);
                    break;
            }
        }

        return new HotkeyCombination
        {
            Modifiers = modifiers,
            VirtualKey = virtualKey,
            OriginalString = hotkeyString
        };
    }

    /// <summary>
    /// Converts a key name to a virtual key code.
    /// </summary>
    private static uint GetVirtualKey(string keyName)
    {
        // Try to parse as a single character (letter or number)
        if (keyName.Length == 1)
        {
            char c = keyName[0];

            // Letters A-Z
            if (c >= 'A' && c <= 'Z')
                return (uint)c;

            // Numbers 0-9
            if (c >= '0' && c <= '9')
                return (uint)c;
        }

        // Try to parse as a Key enum
        if (Enum.TryParse<Key>(keyName, true, out var key))
        {
            return (uint)KeyInterop.VirtualKeyFromKey(key);
        }

        // Handle specific key names
        return keyName.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" => 0x1B,
            "ESC" => 0x1B,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "DEL" => 0x2E,
            "INSERT" => 0x2D,
            "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PGUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "NUMPAD0" => 0x60,
            "NUMPAD1" => 0x61,
            "NUMPAD2" => 0x62,
            "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65,
            "NUMPAD6" => 0x66,
            "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68,
            "NUMPAD9" => 0x69,
            "MULTIPLY" => 0x6A,
            "ADD" => 0x6B,
            "SUBTRACT" => 0x6D,
            "DECIMAL" => 0x6E,
            "DIVIDE" => 0x6F,
            _ => 0
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAll();
        _windowHandle = IntPtr.Zero;

        GC.SuppressFinalize(this);
    }
}
