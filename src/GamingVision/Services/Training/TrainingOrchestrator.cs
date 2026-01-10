using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision.Services.Training;

/// <summary>
/// Training mode selection.
/// </summary>
public enum TrainingMode
{
    /// <summary>
    /// Fine-tune existing model with new data only.
    /// </summary>
    FineTune,

    /// <summary>
    /// Full retrain from scratch using all accumulated data.
    /// </summary>
    FullRetrain
}

/// <summary>
/// Result of a training operation.
/// </summary>
public class TrainingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ModelPath { get; set; }
    public List<string> NewClasses { get; set; } = [];
    public int ExitCode { get; set; }
}

/// <summary>
/// Orchestrates the training workflow: split, train, export, and class detection.
/// </summary>
public class TrainingOrchestrator : IDisposable
{
    private readonly PythonService _pythonService;
    private readonly ConfigManager _configManager;
    private bool _disposed;

    /// <summary>
    /// Event raised when progress changes.
    /// </summary>
    public event EventHandler<string>? ProgressChanged;

    /// <summary>
    /// Event raised when log output is received.
    /// </summary>
    public event EventHandler<string>? LogOutput;

    public TrainingOrchestrator(ConfigManager configManager)
    {
        _configManager = configManager;
        _pythonService = new PythonService();
        _pythonService.OutputReceived += (s, e) => OnLogOutput(e);
        _pythonService.ErrorReceived += (s, e) => OnLogOutput($"[ERR] {e}");
    }

    /// <summary>
    /// Runs the training workflow based on configuration.
    /// </summary>
    public async Task<TrainingResult> RunTrainingAsync(
        string gameId,
        string modelName,
        TrainingMode mode,
        string newTrainingDataPath,
        string baseTrainingDataPath,
        string gameModelsPath,
        ModelTrainingParameters trainingParams,
        CancellationToken cancellationToken)
    {
        var result = new TrainingResult();
        var scriptsDir = PythonService.GetScriptsDirectory();

        if (string.IsNullOrEmpty(scriptsDir))
        {
            result.Message = "Could not find training scripts directory";
            return result;
        }

        try
        {
            OnProgressChanged("Checking Python installation...");

            var (pythonAvailable, pythonVersion, _) = await _pythonService.DetectPythonAsync();
            if (!pythonAvailable)
            {
                result.Message = $"Python 3.10 not found. Please install Python 3.10 from python.org.\n{pythonVersion}";
                return result;
            }

            OnLogOutput($"Python detected: {pythonVersion}");

            // Determine training data path based on mode
            string trainingDataPath;
            string? fineTuneModelPath = null;

            if (mode == TrainingMode.FineTune)
            {
                trainingDataPath = newTrainingDataPath;
                fineTuneModelPath = Path.Combine(gameModelsPath, "best.pt");

                if (!File.Exists(fineTuneModelPath))
                {
                    result.Message = $"Fine-tune model not found: {fineTuneModelPath}\nPlease run a full training first.";
                    return result;
                }
            }
            else // FullRetrain
            {
                trainingDataPath = baseTrainingDataPath;
            }

            // Validate training data exists
            var imagesPath = Path.Combine(trainingDataPath, "images");
            var labelsPath = Path.Combine(trainingDataPath, "labels");
            var classesFile = Path.Combine(trainingDataPath, "classes.txt");

            if (!Directory.Exists(imagesPath) || !Directory.GetFiles(imagesPath).Any())
            {
                result.Message = $"No images found in: {imagesPath}";
                return result;
            }

            if (!Directory.Exists(labelsPath) || !Directory.GetFiles(labelsPath, "*.txt").Any())
            {
                result.Message = $"No labels found in: {labelsPath}\nPlease annotate images with mlabelImg first.";
                return result;
            }

            if (!File.Exists(classesFile))
            {
                result.Message = $"classes.txt not found in: {trainingDataPath}\nPlease ensure annotation is complete.";
                return result;
            }

            // Build arguments for the pipeline script
            var pipelineScript = Path.Combine(scriptsDir, "01_train_pipeline.py");
            if (!File.Exists(pipelineScript))
            {
                result.Message = $"Pipeline script not found: {pipelineScript}";
                return result;
            }

            var args = new List<string>
            {
                "--auto-yes",
                $"--game-id \"{gameId}\"",
                $"--model-name \"{modelName}\"",
                $"--training-data-path \"{trainingDataPath}\"",
                $"--game-models-path \"{gameModelsPath}\"",
                // Training parameters
                $"--epochs {trainingParams.Epochs}",
                $"--imgsz {trainingParams.ImageSize}",
                $"--batch {trainingParams.Batch:F2}",
                $"--patience {trainingParams.Patience}",
                $"--lr0 {trainingParams.LearningRate:F6}",
                $"--device \"{trainingParams.Device}\"",
                $"--workers {trainingParams.Workers}",
                trainingParams.CacheImages ? "--cache" : "--no-cache",
                trainingParams.UseMixedPrecision ? "--amp" : "--no-amp"
            };

            if (mode == TrainingMode.FineTune && fineTuneModelPath != null)
            {
                args.Add($"--fine-tune-model-path \"{fineTuneModelPath}\"");
            }

            OnProgressChanged($"Starting {(mode == TrainingMode.FineTune ? "fine-tune" : "full")} training...");
            OnLogOutput($"Training data: {trainingDataPath}");
            OnLogOutput($"Output: {gameModelsPath}");

            // Execute the pipeline
            var exitCode = await _pythonService.ExecuteScriptAsync(
                pipelineScript,
                string.Join(" ", args),
                scriptsDir,
                cancellationToken);

            result.ExitCode = exitCode;

            if (exitCode == 0)
            {
                OnProgressChanged("Training completed successfully!");
                result.Success = true;
                result.Message = "Training completed successfully";

                // Detect new classes
                result.NewClasses = await DetectNewClassesAsync(classesFile, gameId);
                if (result.NewClasses.Count > 0)
                {
                    OnLogOutput($"New classes detected: {string.Join(", ", result.NewClasses)}");
                }
            }
            else
            {
                result.Message = $"Training failed with exit code: {exitCode}";
                OnProgressChanged($"Training failed (exit code {exitCode})");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            result.Message = "Training was cancelled";
            OnProgressChanged("Training cancelled");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"Training orchestration failed: {ex.Message}");
            result.Message = $"Training failed: {ex.Message}";
            OnProgressChanged($"Error: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Detects classes in classes.txt that aren't in game_config.json.
    /// </summary>
    public async Task<List<string>> DetectNewClassesAsync(string classesFile, string gameId)
    {
        var newClasses = new List<string>();

        try
        {
            if (!File.Exists(classesFile))
                return newClasses;

            var classesInFile = (await File.ReadAllLinesAsync(classesFile))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToHashSet();

            var profile = await _configManager.LoadGameProfileAsync(gameId);
            if (profile?.Labels == null)
                return classesInFile.ToList();

            var existingClasses = profile.Labels.Select(l => l.Name).ToHashSet();

            foreach (var cls in classesInFile)
            {
                if (!existingClasses.Contains(cls))
                {
                    newClasses.Add(cls);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to detect new classes: {ex.Message}");
        }

        return newClasses;
    }

    /// <summary>
    /// Adds new label entries to game_config.json (name only, user assigns tiers).
    /// </summary>
    public async Task AddNewLabelsToConfigAsync(string gameId, List<string> newLabels)
    {
        if (newLabels.Count == 0)
            return;

        try
        {
            var profile = await _configManager.LoadGameProfileAsync(gameId);
            if (profile == null)
            {
                Logger.Error($"Could not load profile for {gameId}");
                return;
            }

            profile.Labels ??= [];

            foreach (var label in newLabels)
            {
                if (!profile.Labels.Any(l => l.Name == label))
                {
                    profile.Labels.Add(new LabelDefinition
                    {
                        Name = label,
                        Description = $"New label: {label}"
                    });
                    OnLogOutput($"Added new label: {label}");
                }
            }

            await _configManager.SaveGameProfileAsync(profile);
            OnLogOutput($"Updated game_config.json with {newLabels.Count} new labels");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to add new labels: {ex.Message}");
        }
    }

    /// <summary>
    /// Merges New_Training_Data into Base_Training_Data.
    /// </summary>
    public async Task<(int ImagesMoved, int LabelsMoved)> MergeNewToBaseAsync(
        string newPath,
        string basePath)
    {
        var imagesMoved = 0;
        var labelsMoved = 0;

        try
        {
            var newImagesDir = Path.Combine(newPath, "images");
            var newLabelsDir = Path.Combine(newPath, "labels");
            var baseImagesDir = Path.Combine(basePath, "images");
            var baseLabelsDir = Path.Combine(basePath, "labels");

            // Create base directories if they don't exist
            Directory.CreateDirectory(baseImagesDir);
            Directory.CreateDirectory(baseLabelsDir);

            // Move images
            if (Directory.Exists(newImagesDir))
            {
                foreach (var file in Directory.GetFiles(newImagesDir))
                {
                    var destFile = Path.Combine(baseImagesDir, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                    {
                        File.Move(file, destFile);
                        imagesMoved++;
                    }
                    else
                    {
                        // File exists, generate unique name
                        var baseName = Path.GetFileNameWithoutExtension(file);
                        var ext = Path.GetExtension(file);
                        var uniqueName = $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
                        File.Move(file, Path.Combine(baseImagesDir, uniqueName));
                        imagesMoved++;
                    }
                }
            }

            // Move labels
            if (Directory.Exists(newLabelsDir))
            {
                foreach (var file in Directory.GetFiles(newLabelsDir, "*.txt"))
                {
                    var destFile = Path.Combine(baseLabelsDir, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                    {
                        File.Move(file, destFile);
                        labelsMoved++;
                    }
                    else
                    {
                        var baseName = Path.GetFileNameWithoutExtension(file);
                        var uniqueName = $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}.txt";
                        File.Move(file, Path.Combine(baseLabelsDir, uniqueName));
                        labelsMoved++;
                    }
                }
            }

            // Merge classes.txt
            var newClassesFile = Path.Combine(newPath, "classes.txt");
            var baseClassesFile = Path.Combine(basePath, "classes.txt");

            if (File.Exists(newClassesFile))
            {
                var newClasses = (await File.ReadAllLinesAsync(newClassesFile))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                var baseClasses = File.Exists(baseClassesFile)
                    ? (await File.ReadAllLinesAsync(baseClassesFile))
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList()
                    : new List<string>();

                // Merge classes (preserve order, add new ones at end)
                foreach (var cls in newClasses)
                {
                    if (!baseClasses.Contains(cls))
                    {
                        baseClasses.Add(cls);
                    }
                }

                await File.WriteAllLinesAsync(baseClassesFile, baseClasses);
            }

            OnLogOutput($"Merged {imagesMoved} images and {labelsMoved} labels to Base folder");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to merge training data: {ex.Message}");
            throw;
        }

        return (imagesMoved, labelsMoved);
    }

    /// <summary>
    /// Clears the New_Training_Data folder (images, labels, train, val).
    /// </summary>
    public void ClearNewTrainingData(string newPath)
    {
        try
        {
            var foldersToClean = new[] { "images", "labels", "train", "val" };

            foreach (var folder in foldersToClean)
            {
                var path = Path.Combine(newPath, folder);
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                }
            }

            // Remove train/val directories entirely
            var trainDir = Path.Combine(newPath, "train");
            var valDir = Path.Combine(newPath, "val");

            if (Directory.Exists(trainDir))
                Directory.Delete(trainDir, recursive: true);
            if (Directory.Exists(valDir))
                Directory.Delete(valDir, recursive: true);

            // Remove dataset.yaml
            var datasetYaml = Path.Combine(newPath, "dataset.yaml");
            if (File.Exists(datasetYaml))
                File.Delete(datasetYaml);

            OnLogOutput("Cleared New_Training_Data folder");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to clear new training data: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up train/val split folders after training.
    /// </summary>
    public void CleanupSplitFolders(string trainingDataPath)
    {
        try
        {
            var trainDir = Path.Combine(trainingDataPath, "train");
            var valDir = Path.Combine(trainingDataPath, "val");
            var datasetYaml = Path.Combine(trainingDataPath, "dataset.yaml");

            if (Directory.Exists(trainDir))
                Directory.Delete(trainDir, recursive: true);
            if (Directory.Exists(valDir))
                Directory.Delete(valDir, recursive: true);
            if (File.Exists(datasetYaml))
                File.Delete(datasetYaml);

            OnLogOutput("Cleaned up train/val split folders");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to cleanup split folders: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the count of images in a training data folder.
    /// </summary>
    public static int GetImageCount(string trainingDataPath)
    {
        var imagesDir = Path.Combine(trainingDataPath, "images");
        if (!Directory.Exists(imagesDir))
            return 0;

        return Directory.GetFiles(imagesDir, "*.jpg").Length +
               Directory.GetFiles(imagesDir, "*.png").Length;
    }

    /// <summary>
    /// Gets the count of labeled images (images with corresponding .txt files).
    /// </summary>
    public static int GetLabeledImageCount(string trainingDataPath)
    {
        var imagesDir = Path.Combine(trainingDataPath, "images");
        var labelsDir = Path.Combine(trainingDataPath, "labels");

        if (!Directory.Exists(imagesDir) || !Directory.Exists(labelsDir))
            return 0;

        var labelFiles = Directory.GetFiles(labelsDir, "*.txt")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet();

        var imageFiles = Directory.GetFiles(imagesDir)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFileNameWithoutExtension(f));

        return imageFiles.Count(img => labelFiles.Contains(img));
    }

    /// <summary>
    /// Cancels the currently running training process.
    /// </summary>
    public void CancelTraining()
    {
        _pythonService.CancelCurrentProcess();
    }

    private void OnProgressChanged(string message)
    {
        Logger.Log($"[Training] {message}");
        ProgressChanged?.Invoke(this, message);
    }

    private void OnLogOutput(string message)
    {
        LogOutput?.Invoke(this, message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pythonService.Dispose();
    }
}
