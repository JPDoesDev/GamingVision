using System.Windows;
using GamingVision.Overlay.Services;

namespace GamingVision.Overlay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize logger first
        OverlayLogger.Initialize();
        OverlayLogger.Log("App", "Application starting...");

        // Add global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            OverlayLogger.Log("FATAL", $"Unhandled exception: {ex?.Message}\n{ex?.StackTrace}");
            MessageBox.Show($"Unhandled exception: {ex?.Message}",
                "GamingVision Overlay Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            OverlayLogger.Log("ERROR", $"Dispatcher exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            MessageBox.Show($"Error: {args.Exception.Message}",
                "GamingVision Overlay Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            OverlayLogger.Log("App", "Main window shown successfully");
        }
        catch (Exception ex)
        {
            OverlayLogger.Log("FATAL", $"Failed to start: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"Failed to start: {ex.Message}",
                "GamingVision Overlay Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OverlayLogger.Log("App", "Application exiting...");
        base.OnExit(e);

        // Force exit after a short delay to ensure cleanup
        Task.Run(async () =>
        {
            await Task.Delay(500);
            Environment.Exit(e.ApplicationExitCode);
        });
    }
}
