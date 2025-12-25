using System.Runtime.InteropServices;
using GamingVision.Native;
using GamingVision.Utilities;

namespace GamingVision.Services.Hotkeys;

/// <summary>
/// Service for handling global hotkeys for overlay toggle.
/// Uses a message-only window for hotkey registration.
/// </summary>
public class OverlayHotkeyService : IDisposable
{
    private const int HOTKEY_ID = 9000;

    private readonly string _hotkeyString;
    private Thread? _messageThread;
    private IntPtr _hwnd;
    private bool _running;
    private bool _disposed;

    // IMPORTANT: Must keep a reference to prevent garbage collection
    private WndProcDelegate? _wndProcDelegate;

    public event EventHandler? HotkeyPressed;

    public OverlayHotkeyService(string hotkeyString)
    {
        _hotkeyString = hotkeyString;
    }

    public void Start()
    {
        if (_running) return;

        _running = true;
        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "OverlayHotkeyThread"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
    }

    public void Stop()
    {
        if (!_running) return;

        try
        {
            Logger.Log("Stop() called, setting _running=false...");
            _running = false;

            if (_hwnd != IntPtr.Zero)
            {
                Logger.Log("Posting WM_QUIT to hotkey window...");
                PostMessage(_hwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            Logger.Log("Waiting for message thread to join...");
            if (_messageThread != null && !_messageThread.Join(500))
            {
                Logger.Log("Thread did not join in time, continuing anyway...");
            }
            _messageThread = null;
            _wndProcDelegate = null;
            Logger.Log("Stop() complete.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in OverlayHotkeyService.Stop(): {ex.Message}", ex);
        }
    }

    private void MessageLoop()
    {
        // Store delegate as field to prevent garbage collection
        _wndProcDelegate = WndProc;

        // Create a message-only window
        var wndClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = GetModuleHandle(null),
            lpszClassName = $"GamingVisionOverlayHotkeyClass_{Environment.TickCount}"
        };

        RegisterClassEx(ref wndClass);

        _hwnd = CreateWindowEx(
            0,
            wndClass.lpszClassName,
            "GamingVisionOverlayHotkey",
            0,
            0, 0, 0, 0,
            HWND_MESSAGE,
            IntPtr.Zero,
            wndClass.hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("Failed to create hotkey window");
            return;
        }

        // Parse and register hotkey
        if (ParseHotkey(_hotkeyString, out uint modifiers, out uint vk))
        {
            if (!User32.RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | User32.MOD_NOREPEAT, vk))
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: {_hotkeyString}");
            }
        }

        // Message loop
        while (_running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // Cleanup
        User32.UnregisterHotKey(_hwnd, HOTKEY_ID);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static bool ParseHotkey(string hotkeyString, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
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
                    // Try to parse as key
                    vk = GetVirtualKeyCode(part);
                    break;
            }
        }

        return vk != 0;
    }

    private static uint GetVirtualKeyCode(string key)
    {
        key = key.ToUpperInvariant();

        // Single character keys
        if (key.Length == 1)
        {
            char c = key[0];
            if (c >= 'A' && c <= 'Z')
                return (uint)c;
            if (c >= '0' && c <= '9')
                return (uint)c;
        }

        // Function keys
        if (key.StartsWith("F") && int.TryParse(key.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24)
        {
            return (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
        }

        // Special keys
        return key switch
        {
            "ESCAPE" or "ESC" => 0x1B,
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            _ => 0
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        GC.SuppressFinalize(this);
    }

    // Win32 API declarations
    private const uint WM_QUIT = 0x0012;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
