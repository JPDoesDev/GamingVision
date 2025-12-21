using System.Runtime.InteropServices;

namespace GamingVision.TrainingTool;

/// <summary>
/// Global hotkey service for console applications using Win32 API.
/// Uses a background thread with a message pump to receive hotkey events.
/// </summary>
public class ConsoleHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_CONTROL = 0x0002;

    // Virtual key codes
    private const uint VK_F1 = 0x70;
    private const uint VK_Q = 0x51;

    private Thread? _messageThread;
    private volatile bool _running;
    private int _hotkeyId = 1;
    private IntPtr _threadId;

    public event Action? F1Pressed;
    public event Action? CtrlQPressed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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

    /// <summary>
    /// Starts listening for global hotkeys.
    /// </summary>
    public void Start()
    {
        if (_running) return;

        _running = true;
        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "HotkeyMessageLoop"
        };
        _messageThread.Start();
    }

    /// <summary>
    /// Stops listening for hotkeys.
    /// </summary>
    public void Stop()
    {
        if (!_running) return;

        _running = false;

        // Post WM_QUIT to break the message loop
        if (_threadId != IntPtr.Zero)
        {
            PostThreadMessage((uint)_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        _messageThread?.Join(1000);
        _messageThread = null;
    }

    private void MessageLoop()
    {
        _threadId = (IntPtr)GetCurrentThreadId();

        // Register hotkeys on this thread
        bool f1Registered = RegisterHotKey(IntPtr.Zero, _hotkeyId, MOD_NONE, VK_F1);
        bool ctrlQRegistered = RegisterHotKey(IntPtr.Zero, _hotkeyId + 1, MOD_CONTROL, VK_Q);

        if (!f1Registered)
        {
            Console.WriteLine("Warning: Could not register F1 hotkey (may be in use by another application)");
        }

        if (!ctrlQRegistered)
        {
            Console.WriteLine("Warning: Could not register Ctrl+Q hotkey");
        }

        try
        {
            while (_running)
            {
                if (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_HOTKEY)
                    {
                        int hotkeyId = (int)msg.wParam;

                        if (hotkeyId == _hotkeyId)
                        {
                            // F1 pressed
                            F1Pressed?.Invoke();
                        }
                        else if (hotkeyId == _hotkeyId + 1)
                        {
                            // Ctrl+Q pressed
                            CtrlQPressed?.Invoke();
                        }
                    }

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                else
                {
                    // WM_QUIT received
                    break;
                }
            }
        }
        finally
        {
            // Unregister hotkeys
            UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            UnregisterHotKey(IntPtr.Zero, _hotkeyId + 1);
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
