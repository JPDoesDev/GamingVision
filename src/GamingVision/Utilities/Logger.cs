using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace GamingVision.Utilities;

/// <summary>
/// Log category for multi-file logging.
/// </summary>
public enum LogCategory
{
    General,
    Performance,
    Error
}

/// <summary>
/// Simple file logger for debugging application flow.
/// Supports multiple log categories with separate files.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static readonly object _perfLock = new();
    private static readonly object _errorLock = new();
    private static string? _logFilePath;
    private static string? _perfLogFilePath;
    private static string? _errorLogFilePath;
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

            // Set up additional log files in the same directory
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                _perfLogFilePath = Path.Combine(directory, "performance.log");
                _errorLogFilePath = Path.Combine(directory, "error.log");

                // Create directory if it doesn't exist
                if (!Directory.Exists(directory))
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

    #region Performance Logging

    /// <summary>
    /// Logs a performance message to the dedicated performance log file.
    /// </summary>
    public static void Perf(string message)
    {
        WritePerfLog(message);
    }

    /// <summary>
    /// Logs a frame-correlated performance message with stage information.
    /// Format: [Frame #N] [STAGE] message
    /// </summary>
    /// <param name="frameId">The frame identifier for correlation.</param>
    /// <param name="stage">The pipeline stage (e.g., CAPTURE, DETECT, RENDER).</param>
    /// <param name="message">The log message.</param>
    public static void PerfFrame(ulong frameId, string stage, string message)
    {
        WritePerfLog($"[Frame #{frameId}] [{stage}] {message}");
    }

    /// <summary>
    /// Logs a frame-correlated performance message with elapsed time from frame start.
    /// Format: [Frame #N] [T+X.Xms] [STAGE] message
    /// </summary>
    /// <param name="frameId">The frame identifier for correlation.</param>
    /// <param name="captureStartTicks">High-precision timestamp from Stopwatch.GetTimestamp() at frame capture start.</param>
    /// <param name="stage">The pipeline stage (e.g., CAPTURE, DETECT, RENDER).</param>
    /// <param name="message">The log message.</param>
    public static void PerfFrameTimed(ulong frameId, long captureStartTicks, string stage, string message)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - captureStartTicks;
        var elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;
        WritePerfLog($"[Frame #{frameId}] [T+{elapsedMs:F1}ms] [{stage}] {message}");
    }

    /// <summary>
    /// Logs a frame summary with timing breakdown.
    /// Format: [Frame #N] SUMMARY: capture=Xms, detect=Xms, dispatch=Xms, render=Xms, TOTAL=Xms
    /// </summary>
    public static void PerfFrameSummary(ulong frameId, double captureMs, double detectMs, double dispatchMs, double renderMs, double totalMs)
    {
        WritePerfLog($"[Frame #{frameId}] SUMMARY: capture={captureMs:F1}ms, detect={detectMs:F1}ms, dispatch={dispatchMs:F1}ms, render={renderMs:F1}ms, TOTAL={totalMs:F1}ms");
    }

    /// <summary>
    /// Writes a message to the performance log file.
    /// </summary>
    private static void WritePerfLog(string message)
    {
        if (!_isEnabled || !_isInitialized || string.IsNullOrEmpty(_perfLogFilePath))
            return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] {message}";

        // Write to Debug output
        System.Diagnostics.Debug.WriteLine($"[PERF] {logLine}");

        // Write to performance log file
        lock (_perfLock)
        {
            try
            {
                File.AppendAllText(_perfLogFilePath, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write perf log: {ex.Message}");
            }
        }
    }

    #endregion

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
    /// Clears all log files.
    /// </summary>
    public static void Clear()
    {
        ClearLogFile(_logFilePath, _lock);
        ClearLogFile(_perfLogFilePath, _perfLock);
        ClearLogFile(_errorLogFilePath, _errorLock);
    }

    private static void ClearLogFile(string? filePath, object lockObj)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        lock (lockObj)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.WriteAllText(filePath, string.Empty);
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
