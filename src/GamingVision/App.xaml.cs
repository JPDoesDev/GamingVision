using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize logger from config
        InitializeLogger();

        // Handle exceptions on the UI thread
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Handle exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Handle exceptions in Tasks
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        Logger.Log("Application started");
        LogMessage("Application started");
    }

    private void InitializeLogger()
    {
        try
        {
            // Try to load config to get logging setting
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");
            var enableLogging = false;
            var logFilePath = "logs/gamingvision.log";

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<AppConfiguration>(json, options);
                if (config != null)
                {
                    enableLogging = config.EnableLogging;
                    logFilePath = config.LogFilePath;
                }
            }

            Logger.Initialize(enableLogging, logFilePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
            // Initialize with defaults
            Logger.Initialize(false, "logs/gamingvision.log");
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("UI Thread Exception", e.Exception);
        e.Handled = true; // Prevent crash, but app may be in bad state

        MessageBox.Show(
            $"An error occurred: {e.Exception.Message}\n\nDetails logged to crash_log.txt",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        LogException($"AppDomain Exception (IsTerminating: {e.IsTerminating})", exception);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Unobserved Task Exception", e.Exception);
        e.SetObserved(); // Prevent crash
    }

    private static void LogException(string source, Exception? ex)
    {
        // Log to our Logger
        Logger.Error($"{source}: {ex?.Message}", ex);

        // Also write to crash log for unhandled exceptions
        var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}\n" +
                      $"Type: {ex?.GetType().FullName}\n" +
                      $"Message: {ex?.Message}\n" +
                      $"Stack Trace:\n{ex?.StackTrace}\n" +
                      $"Inner Exception: {ex?.InnerException?.Message}\n" +
                      new string('-', 80) + "\n";

        try
        {
            File.AppendAllText(CrashLogPath, message);
            Debug.WriteLine(message);
        }
        catch
        {
            Debug.WriteLine($"Failed to write to crash log: {message}");
        }
    }

    private static void LogMessage(string message)
    {
        var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n";
        try
        {
            File.AppendAllText(CrashLogPath, logMessage);
        }
        catch
        {
            Debug.WriteLine($"Failed to write to log: {logMessage}");
        }
    }
}
