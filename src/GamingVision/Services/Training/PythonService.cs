using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GamingVision.Utilities;

namespace GamingVision.Services.Training;

/// <summary>
/// Service for detecting Python installation and executing Python scripts.
/// </summary>
public class PythonService : IDisposable
{
    private Process? _currentProcess;
    private bool _disposed;

    /// <summary>
    /// Event raised when output is received from a running process.
    /// </summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>
    /// Event raised when error output is received from a running process.
    /// </summary>
    public event EventHandler<string>? ErrorReceived;

    /// <summary>
    /// Event raised when a process exits.
    /// </summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>
    /// Gets whether a process is currently running.
    /// </summary>
    public bool IsRunning => _currentProcess != null && !_currentProcess.HasExited;

    /// <summary>
    /// Detects if Python is available.
    /// </summary>
    /// <returns>Tuple of (available, version string, python path)</returns>
    public async Task<(bool Available, string Version, string Path)> DetectPythonAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "Failed to start python", string.Empty);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                var error = await process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var version = output.Trim();
                    return (true, version, "python");
                }

                return (false, error.Trim(), string.Empty);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                return (false, "Timeout", string.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Python detection failed: {ex.Message}");
            return (false, ex.Message, string.Empty);
        }
    }

    /// <summary>
    /// Detects CUDA toolkit version via nvcc.
    /// </summary>
    /// <returns>Tuple of (available, version string, isCorrectVersion)</returns>
    public async Task<(bool Available, string Version, bool IsCorrectVersion)> DetectCudaAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvcc",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "Failed to run nvcc", false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    // Parse version from output like "Cuda compilation tools, release 13.0, V13.0.48"
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("release"))
                        {
                            var idx = line.IndexOf("release");
                            if (idx >= 0)
                            {
                                var versionPart = line.Substring(idx);
                                // Check if it's CUDA 13.0
                                var isCorrect = versionPart.Contains("13.0");
                                var parts = versionPart.Split(',');
                                if (parts.Length > 0)
                                {
                                    return (true, parts[0].Trim(), isCorrect);
                                }
                            }
                        }
                    }
                    return (true, "CUDA installed (version unknown)", false);
                }

                return (false, "CUDA toolkit not found", false);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                return (false, "Timeout", false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"CUDA detection failed: {ex.Message}");
            return (false, "CUDA toolkit not found", false);
        }
    }

    /// <summary>
    /// Checks if PyTorch is installed with CUDA support.
    /// </summary>
    /// <returns>Tuple of (available, hasCuda, torchVersion, cudaVersion)</returns>
    public async Task<(bool Available, bool HasCuda, string TorchVersion, string CudaVersion)> DetectPyTorchAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "-c \"import torch; v=torch.version.cuda if torch.cuda.is_available() else 'N/A'; print(f'{torch.__version__}|{torch.cuda.is_available()}|{v}')\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, false, "Check failed", "N/A");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                var error = await process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var parts = output.Trim().Split('|');
                    if (parts.Length >= 3)
                    {
                        var version = parts[0];
                        var hasCuda = parts[1].Equals("True", StringComparison.OrdinalIgnoreCase);
                        var cudaVersion = parts[2];
                        return (true, hasCuda, version, cudaVersion);
                    }
                }

                if (error.Contains("ModuleNotFoundError") || error.Contains("No module named"))
                {
                    return (false, false, "Not installed", "N/A");
                }

                return (false, false, "Check failed", "N/A");
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                return (false, false, "Timeout", "N/A");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"PyTorch detection failed: {ex.Message}");
            return (false, false, "Check failed", "N/A");
        }
    }

    /// <summary>
    /// Checks if a Python package is installed by attempting to import it.
    /// </summary>
    /// <param name="packageName">Name of the package to check</param>
    /// <returns>Tuple of (installed, version)</returns>
    public async Task<(bool Installed, string Version)> CheckPackageAsync(string packageName)
    {
        try
        {
            // Map package names to import names (some differ)
            var importName = packageName.ToLowerInvariant() switch
            {
                "mlabelimg" => "labelImg",
                "pyyaml" => "yaml",
                "pillow" => "PIL",
                _ => packageName
            };

            // Try to import and get version
            var versionAttr = packageName.ToLowerInvariant() switch
            {
                "pyyaml" => "yaml.__version__",
                "pillow" => "PIL.__version__",
                "mlabelimg" => "'installed'",  // labelImg doesn't have __version__
                _ => $"{importName}.__version__"
            };

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"-c \"import {importName}; print({versionAttr})\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "Check failed");

            // Wait for process with timeout
            var completed = process.WaitForExit(5000);
            if (!completed)
            {
                process.Kill();
                return (false, "Timeout");
            }

            if (process.ExitCode == 0)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var version = output.Trim();
                return (true, string.IsNullOrEmpty(version) ? "installed" : version);
            }

            return (false, "Not installed");
        }
        catch (Exception ex)
        {
            Logger.Log($"Package check for {packageName} failed: {ex.Message}");
            return (false, "Check failed");
        }
    }

    /// <summary>
    /// Checks if mlabelImg is installed by attempting to run the executable.
    /// </summary>
    public async Task<bool> IsMLabelImgInstalledAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mlabelImg",
                Arguments = "--help",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            // Wait briefly with timeout
            var completed = process.WaitForExit(5000);
            if (!completed)
            {
                process.Kill();
                return false;
            }

            // If we got here, the executable was found and ran
            // Even if it exits with error (unrecognized args), it means it's installed
            return true;
        }
        catch (Exception ex)
        {
            // Win32Exception "The system cannot find the file specified" means not installed
            Logger.Log($"mlabelImg check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Launches mlabelImg targeting the specified folder.
    /// </summary>
    /// <param name="imagesFolder">Path to images folder</param>
    /// <param name="classesFile">Path to classes.txt file (optional)</param>
    /// <param name="saveDir">Path to save annotations (optional, defaults to labels folder next to images)</param>
    /// <returns>True if launched successfully</returns>
    public async Task<bool> LaunchMLabelImgAsync(string? imagesFolder, string? classesFile = null, string? saveDir = null)
    {
        try
        {
            // Build arguments for mlabelImg
            // mlabelImg [IMAGE_DIR] [PREDEFINED_CLASS_FILE] [SAVE_DIR]
            var args = new StringBuilder();

            if (!string.IsNullOrEmpty(imagesFolder) && Directory.Exists(imagesFolder))
            {
                args.Append($"\"{imagesFolder}\"");

                // If no explicit save dir, use labels folder next to images
                if (string.IsNullOrEmpty(saveDir))
                {
                    var parentDir = Path.GetDirectoryName(imagesFolder);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        saveDir = Path.Combine(parentDir, "labels");
                    }
                }
            }

            if (!string.IsNullOrEmpty(classesFile) && File.Exists(classesFile))
            {
                args.Append($" \"{classesFile}\"");
            }

            if (!string.IsNullOrEmpty(saveDir))
            {
                // Create labels directory if it doesn't exist
                Directory.CreateDirectory(saveDir);
                args.Append($" \"{saveDir}\"");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "mlabelImg",
                Arguments = args.ToString(),
                UseShellExecute = false,
                CreateNoWindow = false
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                Logger.Log("Failed to start mlabelImg");
                return false;
            }

            Logger.Log($"Launched mlabelImg: {psi.Arguments}");

            // Don't wait for it - let it run independently
            // Give it a moment to start
            await Task.Delay(500);

            return !process.HasExited;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to launch mlabelImg: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Executes a Python script with arguments, capturing output.
    /// </summary>
    /// <param name="scriptPath">Full path to the Python script</param>
    /// <param name="arguments">Additional arguments to pass to the script</param>
    /// <param name="workingDirectory">Working directory for the script</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exit code of the process</returns>
    public async Task<int> ExecuteScriptAsync(
        string scriptPath,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            throw new InvalidOperationException("A process is already running. Cancel it first.");
        }

        try
        {
            var fullArgs = $"\"{scriptPath}\" {arguments}";

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = fullArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(scriptPath) ?? ""
            };

            Logger.Log($"Executing: python {fullArgs}");
            OnOutputReceived($"[CMD] python {fullArgs}");

            _currentProcess = new Process { StartInfo = psi };
            _currentProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OnOutputReceived(e.Data);
                }
            };
            _currentProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OnErrorReceived(e.Data);
                }
            };

            _currentProcess.Start();
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

            // Wait for exit or cancellation
            try
            {
                await _currentProcess.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Script execution cancelled by user");
                _currentProcess.Kill(entireProcessTree: true);
                throw;
            }

            var exitCode = _currentProcess.ExitCode;
            OnProcessExited(exitCode);

            Logger.Log($"Script completed with exit code: {exitCode}");
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Script execution failed: {ex.Message}");
            OnErrorReceived($"[ERROR] {ex.Message}");
            return -1;
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }
    }

    /// <summary>
    /// Cancels the currently running process.
    /// </summary>
    public void CancelCurrentProcess()
    {
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            try
            {
                _currentProcess.Kill(entireProcessTree: true);
                Logger.Log("Process cancelled by user");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to kill process: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gets the path to the training scripts directory.
    /// </summary>
    public static string GetScriptsDirectory()
    {
        // First priority: scripts folder next to executable (release build)
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var outputScripts = Path.Combine(exeDir, "scripts");
        if (Directory.Exists(outputScripts))
        {
            return Path.GetFullPath(outputScripts);
        }

        // Development fallback: check for project structure
        var devPath = Path.Combine(exeDir, "..", "..", "..", "..", "..", "src", "GamingVision.TrainingTool", "scripts");
        if (Directory.Exists(devPath))
        {
            return Path.GetFullPath(devPath);
        }

        Logger.Error("Could not find training scripts directory");
        return string.Empty;
    }

    private void OnOutputReceived(string data)
    {
        OutputReceived?.Invoke(this, data);
    }

    private void OnErrorReceived(string data)
    {
        ErrorReceived?.Invoke(this, data);
    }

    private void OnProcessExited(int exitCode)
    {
        ProcessExited?.Invoke(this, exitCode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelCurrentProcess();
        _currentProcess?.Dispose();
    }
}
