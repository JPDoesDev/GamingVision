using System.Windows;
using System.Windows.Interop;
using VIGamingVision.Services.Hotkeys;
using VIGamingVision.ViewModels;

namespace VIGamingVision;

/// <summary>
/// Main window for VIGamingVision application.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly GlobalHotkeyService _hotkeyService;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _hotkeyService = new GlobalHotkeyService();
        _viewModel.SetHotkeyService(_hotkeyService);

        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Get window handle and set up message hook for hotkeys
        var windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(windowHandle);
        _hwndSource?.AddHook(WndProc);

        // Initialize hotkey service with window handle
        _hotkeyService.Initialize(windowHandle);

        await _viewModel.InitializeAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hotkeyService.Dispose();
        _viewModel.Dispose();
    }

    /// <summary>
    /// Window procedure hook to process hotkey messages.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var hotkeyId = _hotkeyService.ProcessMessage(msg, wParam);
        if (hotkeyId.HasValue)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }
}
