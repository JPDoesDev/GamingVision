using System.Windows;

namespace GamingVision.Overlay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Add global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"Unhandled exception: {ex?.Message}",
                "GamingVision Overlay Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Error: {args.Exception.Message}",
                "GamingVision Overlay Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start: {ex.Message}",
                "GamingVision Overlay Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
