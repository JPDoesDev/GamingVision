using System.IO;
using System.Runtime.CompilerServices;

namespace GamingVision.Utilities;

/// <summary>
/// Simple file logger for debugging application flow.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string? _logFilePath;
    private static bool _isEnabled;
    private static bool _isInitialized;

    /// <summary>
    /// Gets whether logging is currently enabled.
    /// </summary>
    public static bool IsEnabled => _isEnabled;

    /// <summary>
    /// Initializes the logger with the specified settings.
    /// </summary>
    public static void Initialize(bool enabled, string logFilePath)
    {
        lock (_lock)
        {
            _isEnabled = enabled;

            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                logFilePath = "logs/gamingvision.log";
            }

            // Make path absolute if relative
            if (!Path.IsPathRooted(logFilePath))
            {
                logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFilePath);
            }

            _logFilePath = logFilePath;

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create log directory: {ex.Message}");
                }
            }

            _isInitialized = true;

            if (_isEnabled)
            {
                Log("Logger initialized", "Logger");
            }
        }
    }

    /// <summary>
    /// Enables or disables logging at runtime.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        if (enabled && _isInitialized)
        {
            Log("Logging enabled", "Logger");
        }
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public static void Log(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog("INFO", message, caller, filePath, lineNumber);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public static void Warn(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog("WARN", message, caller, filePath, lineNumber);
    }

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public static void Error(string message, Exception? ex = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var fullMessage = ex != null
            ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}"
            : message;

        WriteLog("ERROR", fullMessage, caller, filePath, lineNumber);

        if (ex?.StackTrace != null)
        {
            WriteLog("ERROR", $"Stack trace: {ex.StackTrace}", caller, filePath, lineNumber);
        }

        if (ex?.InnerException != null)
        {
            WriteLog("ERROR", $"Inner exception: {ex.InnerException.Message}", caller, filePath, lineNumber);
        }
    }

    /// <summary>
    /// Logs a debug message (only in DEBUG builds).
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void LogDebug(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteLog("DEBUG", message, caller, filePath, lineNumber);
    }

    private static void WriteLog(string level, string message, string caller, string filePath, int lineNumber)
    {
        if (!_isEnabled || !_isInitialized || string.IsNullOrEmpty(_logFilePath))
            return;

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] [{fileName}.{caller}:{lineNumber}] {message}";

        // Always write to Debug output
        System.Diagnostics.Debug.WriteLine(logLine);

        // Write to file
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears the log file.
    /// </summary>
    public static void Clear()
    {
        if (string.IsNullOrEmpty(_logFilePath))
            return;

        lock (_lock)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    File.WriteAllText(_logFilePath, string.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear log: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gets the full path to the log file.
    /// </summary>
    public static string? GetLogFilePath() => _logFilePath;
}
