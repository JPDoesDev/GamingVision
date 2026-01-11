using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingVision.Models;
using GamingVision.Services.Training;
using GamingVision.Utilities;
using GamingVision.Views;
using Microsoft.Win32;

namespace GamingVision.ViewModels;

/// <summary>
/// ViewModel for the Training Window.
/// </summary>
public partial class TrainingWindowViewModel : ObservableObject, IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly GameProfile _profile;
    private readonly PythonService _pythonService;
    private bool _disposed;

    #region Observable Properties

    [ObservableProperty]
    private string _newTrainingDataPath = string.Empty;

    [ObservableProperty]
    private string _baseTrainingDataPath = string.Empty;

    [ObservableProperty]
    private int _newImageCount;

    [ObservableProperty]
    private int _newLabeledCount;

    [ObservableProperty]
    private int _baseImageCount;

    [ObservableProperty]
    private int _baseLabeledCount;

    [ObservableProperty]
    private bool _isFineTuneMode;

    [ObservableProperty]
    private bool _isFullRetrainMode = true;

    [ObservableProperty]
    private string _modeDescription = string.Empty;

    [ObservableProperty]
    private string _pythonStatus = "Checking Python...";

    [ObservableProperty]
    private bool _canLaunchMLabelImg;

    [ObservableProperty]
    private bool _canSelectMode = true;

    [ObservableProperty]
    private bool _canStartTraining;

    [ObservableProperty]
    private bool _canMerge;

    [ObservableProperty]
    private bool _canClearNew;

    // Prerequisite check properties
    [ObservableProperty]
    private bool _isCheckingPrerequisites = true;

    [ObservableProperty]
    private bool _prerequisitesChecked;

    [ObservableProperty]
    private bool _allPrerequisitesMet;

    [ObservableProperty]
    private string _pythonCheckStatus = "Checking...";

    [ObservableProperty]
    private string _pythonCheckIcon = "?";

    [ObservableProperty]
    private bool _pythonCheckPassed;

    [ObservableProperty]
    private string _cudaCheckStatus = "Checking...";

    [ObservableProperty]
    private string _cudaCheckIcon = "?";

    [ObservableProperty]
    private bool _cudaCheckPassed;

    [ObservableProperty]
    private string _pytorchCheckStatus = "Checking...";

    [ObservableProperty]
    private string _pytorchCheckIcon = "?";

    [ObservableProperty]
    private bool _pytorchCheckPassed;

    [ObservableProperty]
    private string _packagesCheckStatus = "Checking...";

    [ObservableProperty]
    private string _packagesCheckIcon = "?";

    [ObservableProperty]
    private bool _packagesCheckPassed;

    [ObservableProperty]
    private string _missingPackages = string.Empty;

    #endregion

    #region Constants

    private const string PythonDownloadUrl = "https://www.python.org/downloads/release/python-3100/";
    private const string CudaDownloadUrl = "https://developer.nvidia.com/cuda-13-0-0-download-archive";

    #endregion

    public TrainingWindowViewModel(ConfigManager configManager, GameProfile profile)
    {
        _configManager = configManager;
        _profile = profile;

        // Initialize Python service for prerequisite checks and mlabelImg
        _pythonService = new PythonService();

        // Set paths
        var trainingSettings = profile.Training ?? new TrainingSettings();
        NewTrainingDataPath = trainingSettings.GetNewTrainingDataPath(profile.GameId);
        BaseTrainingDataPath = trainingSettings.GetBaseTrainingDataPath(profile.GameId);

        // Update mode description
        UpdateModeDescription();

        // Initial state
        RefreshCounts();

        // Check prerequisites on load
        _ = CheckPrerequisitesAsync();
    }

    partial void OnIsFineTuneModeChanged(bool value)
    {
        if (value)
        {
            IsFullRetrainMode = false;
        }
        UpdateModeDescription();
        ValidateCanTrain();
    }

    partial void OnIsFullRetrainModeChanged(bool value)
    {
        if (value)
        {
            IsFineTuneMode = false;
        }
        UpdateModeDescription();
        ValidateCanTrain();
    }

    private void UpdateModeDescription()
    {
        if (IsFineTuneMode)
        {
            ModeDescription = "Fine-tuning uses the existing trained model as a starting point and trains " +
                              "only on new data. This is faster and helps the model learn new examples " +
                              "while retaining most of its previous knowledge.";
        }
        else
        {
            ModeDescription = "Full retraining trains a new model from scratch using all accumulated data " +
                              "in the Base folder. This takes longer but can produce better results when " +
                              "you have significant new data or want to fix issues with the existing model.";
        }
    }

    private async Task CheckPrerequisitesAsync()
    {
        IsCheckingPrerequisites = true;
        PrerequisitesChecked = false;
        AllPrerequisitesMet = false;

        try
        {
            // Check Python 3.10
            await CheckPython310Async();

            // Check CUDA 13.0
            await CheckCuda130Async();

            // Check PyTorch with CUDA
            await CheckPyTorchAsync();

            // Check required packages
            await CheckRequiredPackagesAsync();

            // Determine if all prerequisites are met
            AllPrerequisitesMet = PythonCheckPassed && CudaCheckPassed && PytorchCheckPassed && PackagesCheckPassed;

            // Update Python status for mlabelImg button
            if (PythonCheckPassed)
            {
                var mlabelInstalled = await _pythonService.IsMLabelImgInstalledAsync();
                if (mlabelInstalled)
                {
                    PythonStatus = "mlabelImg ready";
                    CanLaunchMLabelImg = true;
                }
                else
                {
                    PythonStatus = "mlabelImg not installed. Run: python -m pip install mlabelImg";
                    CanLaunchMLabelImg = false;
                }
            }
            else
            {
                PythonStatus = "Python 3.10 required";
                CanLaunchMLabelImg = false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Prerequisite check failed: {ex.Message}");
        }
        finally
        {
            IsCheckingPrerequisites = false;
            PrerequisitesChecked = true;
        }
    }

    private async Task CheckPython310Async()
    {
        var (available, version, _) = await _pythonService.DetectPythonAsync();

        if (available && version.Contains("3.10"))
        {
            PythonCheckStatus = version;
            PythonCheckIcon = "[OK]";
            PythonCheckPassed = true;
        }
        else if (available)
        {
            PythonCheckStatus = $"{version} (need 3.10)";
            PythonCheckIcon = "[X]";
            PythonCheckPassed = false;
        }
        else
        {
            PythonCheckStatus = "Not installed";
            PythonCheckIcon = "[X]";
            PythonCheckPassed = false;
        }
    }

    private async Task CheckCuda130Async()
    {
        var (available, version, isCorrect) = await _pythonService.DetectCudaAsync();

        if (available && isCorrect)
        {
            CudaCheckStatus = version;
            CudaCheckIcon = "[OK]";
            CudaCheckPassed = true;
        }
        else if (available)
        {
            CudaCheckStatus = $"{version} (need 13.0)";
            CudaCheckIcon = "[X]";
            CudaCheckPassed = false;
        }
        else
        {
            CudaCheckStatus = "Not installed";
            CudaCheckIcon = "[X]";
            CudaCheckPassed = false;
        }
    }

    private async Task CheckPyTorchAsync()
    {
        if (!PythonCheckPassed)
        {
            PytorchCheckStatus = "Requires Python 3.10";
            PytorchCheckIcon = "[X]";
            PytorchCheckPassed = false;
            return;
        }

        var (available, hasCuda, torchVersion, cudaVersion) = await _pythonService.DetectPyTorchAsync();

        if (available && hasCuda)
        {
            PytorchCheckStatus = $"v{torchVersion} (CUDA {cudaVersion})";
            PytorchCheckIcon = "[OK]";
            PytorchCheckPassed = true;
        }
        else if (available)
        {
            PytorchCheckStatus = $"v{torchVersion} (CPU only - no CUDA)";
            PytorchCheckIcon = "[X]";
            PytorchCheckPassed = false;
        }
        else
        {
            PytorchCheckStatus = "Not installed";
            PytorchCheckIcon = "[X]";
            PytorchCheckPassed = false;
        }
    }

    private async Task CheckRequiredPackagesAsync()
    {
        if (!PythonCheckPassed)
        {
            PackagesCheckStatus = "Requires Python 3.10";
            PackagesCheckIcon = "[X]";
            PackagesCheckPassed = false;
            return;
        }

        var requiredPackages = new[] { "ultralytics", "pyyaml", "pillow" };
        var missing = new List<string>();

        foreach (var package in requiredPackages)
        {
            var (installed, _) = await _pythonService.CheckPackageAsync(package);
            if (!installed)
            {
                missing.Add(package);
            }
        }

        // Check mlabelImg separately using executable detection
        var mlabelImgInstalled = await _pythonService.IsMLabelImgInstalledAsync();
        if (!mlabelImgInstalled)
        {
            missing.Add("mlabelImg");
        }

        if (missing.Count == 0)
        {
            PackagesCheckStatus = "All packages installed";
            PackagesCheckIcon = "[OK]";
            PackagesCheckPassed = true;
            MissingPackages = string.Empty;
        }
        else
        {
            PackagesCheckStatus = $"Missing: {string.Join(", ", missing)}";
            PackagesCheckIcon = "[X]";
            PackagesCheckPassed = false;
            MissingPackages = string.Join(", ", missing);
        }
    }

    [RelayCommand]
    private void OpenPythonDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PythonDownloadUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open Python download URL: {ex.Message}");
            MessageBox.Show($"Failed to open browser. Please visit:\n{PythonDownloadUrl}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenCudaDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CudaDownloadUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open CUDA download URL: {ex.Message}");
            MessageBox.Show($"Failed to open browser. Please visit:\n{CudaDownloadUrl}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task InstallPackagesAsync()
    {
        if (!PythonCheckPassed)
        {
            MessageBox.Show("Python 3.10 must be installed first.", "Cannot Install Packages",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var scriptsDir = PythonService.GetScriptsDirectory();
        var requirementsPath = Path.Combine(scriptsDir, "requirements.txt");

        if (!File.Exists(requirementsPath))
        {
            MessageBox.Show($"requirements.txt not found at:\n{requirementsPath}",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var result = MessageBox.Show(
            "This will open a terminal window to install required Python packages.\n\n" +
            "The window will stay open so you can see the results.\n" +
            "Close the terminal window when done, then click 'Recheck Prerequisites'.\n\n" +
            "Continue?",
            "Install Packages",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            // Launch visible terminal with /k to keep it open after command completes
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k python -m pip install -r \"{requirementsPath}\"",
                UseShellExecute = true,
                WorkingDirectory = scriptsDir
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Error($"Package installation failed: {ex.Message}");
            MessageBox.Show($"Failed to open terminal:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RecheckPrerequisitesAsync()
    {
        await CheckPrerequisitesAsync();
    }

    [RelayCommand]
    private void BrowseNewPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select New Training Data Folder",
            InitialDirectory = Directory.Exists(NewTrainingDataPath)
                ? NewTrainingDataPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == true)
        {
            NewTrainingDataPath = dialog.FolderName;
            RefreshCounts();
        }
    }

    [RelayCommand]
    private void BrowseBasePath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Base Training Data Folder",
            InitialDirectory = Directory.Exists(BaseTrainingDataPath)
                ? BaseTrainingDataPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == true)
        {
            BaseTrainingDataPath = dialog.FolderName;
            RefreshCounts();
        }
    }

    [RelayCommand]
    private void RefreshCounts()
    {
        // New Training Data
        if (Directory.Exists(NewTrainingDataPath))
        {
            NewImageCount = TrainingOrchestrator.GetImageCount(NewTrainingDataPath);
            NewLabeledCount = TrainingOrchestrator.GetLabeledImageCount(NewTrainingDataPath);
        }
        else
        {
            NewImageCount = 0;
            NewLabeledCount = 0;
        }

        // Base Training Data
        if (Directory.Exists(BaseTrainingDataPath))
        {
            BaseImageCount = TrainingOrchestrator.GetImageCount(BaseTrainingDataPath);
            BaseLabeledCount = TrainingOrchestrator.GetLabeledImageCount(BaseTrainingDataPath);
        }
        else
        {
            BaseImageCount = 0;
            BaseLabeledCount = 0;
        }

        // Update button states
        CanMerge = NewImageCount > 0;
        CanClearNew = NewImageCount > 0 || NewLabeledCount > 0;
        ValidateCanTrain();
    }

    private void ValidateCanTrain()
    {
        if (IsFineTuneMode)
        {
            // Fine-tune needs new data and an existing .pt model
            var latestPt = GetLatestPtModel();
            CanStartTraining = NewLabeledCount > 0 && latestPt != null;
        }
        else
        {
            // Full retrain needs base data
            CanStartTraining = BaseLabeledCount > 0;
        }
    }

    /// <summary>
    /// Finds the latest .pt model file in the GameModels directory for fine-tuning.
    /// Returns null if no .pt file exists.
    /// </summary>
    private string? GetLatestPtModel()
    {
        var gameModelsDir = Path.Combine(_configManager.GameModelsDirectory, _profile.GameId);
        if (!Directory.Exists(gameModelsDir))
            return null;

        // Find all .pt files and get the most recently modified one
        var ptFiles = Directory.GetFiles(gameModelsDir, "*.pt")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        return ptFiles;
    }

    [RelayCommand]
    private async Task LaunchMLabelImgAsync()
    {
        try
        {
            // Determine which folder to open based on where images exist
            string? targetPath = null;
            string? imagesPath = null;

            // Priority 1: New training data has images
            var newImagesPath = Path.Combine(NewTrainingDataPath, "images");
            if (Directory.Exists(newImagesPath) && Directory.GetFiles(newImagesPath, "*.*").Length > 0)
            {
                targetPath = NewTrainingDataPath;
                imagesPath = newImagesPath;
            }
            // Priority 2: Base training data has images
            else
            {
                var baseImagesPath = Path.Combine(BaseTrainingDataPath, "images");
                if (Directory.Exists(baseImagesPath) && Directory.GetFiles(baseImagesPath, "*.*").Length > 0)
                {
                    targetPath = BaseTrainingDataPath;
                    imagesPath = baseImagesPath;
                }
            }
            // Priority 3: No images anywhere - launch with no arguments

            // Find classes file in labels folder - check target folder first, then fall back to other folder
            // mlabelImg saves classes.txt in the labels folder
            string? classesFile = null;
            if (targetPath != null)
            {
                var targetClassesFile = Path.Combine(targetPath, "labels", "classes.txt");
                if (File.Exists(targetClassesFile))
                {
                    classesFile = targetClassesFile;
                }
            }

            // Fallback: check both labels folders for classes.txt
            if (classesFile == null)
            {
                var newClassesFile = Path.Combine(NewTrainingDataPath, "labels", "classes.txt");
                var baseClassesFile = Path.Combine(BaseTrainingDataPath, "labels", "classes.txt");

                if (File.Exists(newClassesFile))
                    classesFile = newClassesFile;
                else if (File.Exists(baseClassesFile))
                    classesFile = baseClassesFile;
            }

            var success = await _pythonService.LaunchMLabelImgAsync(imagesPath, classesFile);

            if (!success)
            {
                MessageBox.Show("Failed to launch mlabelImg.\nMake sure it's installed: python -m pip install mlabelImg",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to launch mlabelImg: {ex.Message}");
            MessageBox.Show($"Failed to launch mlabelImg:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task MergeToBaseAsync()
    {
        var result = MessageBox.Show(
            $"This will move {NewImageCount} images and {NewLabeledCount} labels from New to Base folder.\n\n" +
            "This action cannot be undone. Continue?",
            "Confirm Merge",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var (imagesMoved, labelsMoved) = await MergeNewToBaseInternalAsync(NewTrainingDataPath, BaseTrainingDataPath);
            RefreshCounts();

            MessageBox.Show($"Merge complete!\n\n{imagesMoved} images and {labelsMoved} labels moved to Base folder.",
                "Merge Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"Merge failed: {ex.Message}");
            MessageBox.Show($"Merge failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<(int ImagesMoved, int LabelsMoved)> MergeNewToBaseInternalAsync(string newPath, string basePath)
    {
        var imagesMoved = 0;
        var labelsMoved = 0;

        var newImagesDir = Path.Combine(newPath, "images");
        var newLabelsDir = Path.Combine(newPath, "labels");
        var baseImagesDir = Path.Combine(basePath, "images");
        var baseLabelsDir = Path.Combine(basePath, "labels");

        // Create base directories if they don't exist
        Directory.CreateDirectory(baseImagesDir);
        Directory.CreateDirectory(baseLabelsDir);

        // Move images and their corresponding labels together to keep them paired
        if (Directory.Exists(newImagesDir))
        {
            foreach (var imageFile in Directory.GetFiles(newImagesDir))
            {
                var fileName = Path.GetFileName(imageFile);
                var baseName = Path.GetFileNameWithoutExtension(imageFile);
                var ext = Path.GetExtension(imageFile);
                var destImageFile = Path.Combine(baseImagesDir, fileName);

                // Check if we need a unique name
                string finalBaseName = baseName;
                if (File.Exists(destImageFile))
                {
                    // Generate unique name for both image and label
                    finalBaseName = $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}";
                    destImageFile = Path.Combine(baseImagesDir, $"{finalBaseName}{ext}");
                }

                // Move image
                File.Move(imageFile, destImageFile);
                imagesMoved++;

                // Move corresponding label if it exists (using the same final name)
                var labelFile = Path.Combine(newLabelsDir, $"{baseName}.txt");
                if (File.Exists(labelFile))
                {
                    var destLabelFile = Path.Combine(baseLabelsDir, $"{finalBaseName}.txt");
                    File.Move(labelFile, destLabelFile);
                    labelsMoved++;
                }
            }
        }

        // Move any orphaned labels (labels without matching images)
        if (Directory.Exists(newLabelsDir))
        {
            foreach (var labelFile in Directory.GetFiles(newLabelsDir, "*.txt"))
            {
                var fileName = Path.GetFileName(labelFile);
                var destLabelFile = Path.Combine(baseLabelsDir, fileName);

                if (!File.Exists(destLabelFile))
                {
                    File.Move(labelFile, destLabelFile);
                    labelsMoved++;
                }
                else
                {
                    var baseName = Path.GetFileNameWithoutExtension(labelFile);
                    var uniqueName = $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}.txt";
                    File.Move(labelFile, Path.Combine(baseLabelsDir, uniqueName));
                    labelsMoved++;
                }
            }
        }

        // Merge classes.txt (located in labels folder)
        var newClassesFile = Path.Combine(newLabelsDir, "classes.txt");
        var baseClassesFile = Path.Combine(baseLabelsDir, "classes.txt");

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

        return (imagesMoved, labelsMoved);
    }

    [RelayCommand]
    private void ClearNew()
    {
        var result = MessageBox.Show(
            $"This will delete all {NewImageCount} images and {NewLabeledCount} labels from the New folder.\n\n" +
            "This action cannot be undone. Continue?",
            "Confirm Clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            ClearNewTrainingDataInternal(NewTrainingDataPath);
            RefreshCounts();

            MessageBox.Show("New Training Data folder cleared.",
                "Clear Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"Clear failed: {ex.Message}");
            MessageBox.Show($"Clear failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ClearNewTrainingDataInternal(string newPath)
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
    }

    [RelayCommand]
    private async Task StartTrainingAsync()
    {
        // Validate
        if (IsFineTuneMode && NewLabeledCount == 0)
        {
            MessageBox.Show(
                "No labeled images in New Training Data folder.\n" +
                "Please capture and annotate images first.",
                "Cannot Start Training",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (IsFullRetrainMode && BaseLabeledCount == 0)
        {
            MessageBox.Show(
                "No labeled images in Base Training Data folder.\n" +
                "Please add training data first.",
                "Cannot Start Training",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Warn if Full Retrain but New folder has images that won't be used
        if (IsFullRetrainMode && NewLabeledCount > 0)
        {
            var result = MessageBox.Show(
                $"You have {NewLabeledCount} labeled images in the New Training Data folder.\n\n" +
                "Full Retrain only uses images from the Base folder.\n" +
                "These new images will NOT be included in training.\n\n" +
                "To include them, click 'Merge New → Base' first.\n\n" +
                "Continue with Full Retrain anyway?",
                "New Images Won't Be Used",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        // Warn about CPU mode if GPU prerequisites aren't met
        if (!AllPrerequisitesMet)
        {
            var prereqDialog = new PrerequisitesDialog
            {
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current.MainWindow
            };

            // Set status for each prerequisite
            prereqDialog.SetPythonStatus(PythonCheckPassed, PythonCheckStatus);
            prereqDialog.SetCudaStatus(CudaCheckPassed, CudaCheckStatus);
            prereqDialog.SetPytorchStatus(PytorchCheckPassed, PytorchCheckStatus);
            prereqDialog.SetPackagesStatus(PackagesCheckPassed, PackagesCheckStatus);

            // Set install actions
            prereqDialog.InstallPackagesAction = async () =>
            {
                await InstallPackagesAsync();
                await CheckPrerequisitesAsync();
            };

            var dialogResult = prereqDialog.ShowDialog();

            if (dialogResult != true || !prereqDialog.ContinueWithCpu)
                return;

            // GPU prerequisites not met - training will use CPU mode (handled in parameters dialog)
        }

        // Show training parameters dialog
        var mode = IsFineTuneMode ? TrainingMode.FineTune : TrainingMode.FullRetrain;
        var existingParams = _profile.Training?.TrainingParameters;

        var paramsDialog = new TrainingParametersWindow(existingParams, mode)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow
        };

        if (paramsDialog.ShowDialog() != true || !paramsDialog.StartTrainingConfirmed)
        {
            return; // User cancelled
        }

        var trainingParams = paramsDialog.Parameters!;

        // Save parameters to profile for next time
        _profile.Training ??= new TrainingSettings();
        _profile.Training.TrainingParameters = trainingParams.Clone();
        await _configManager.SaveGameProfileAsync(_profile);

        // Validate prerequisites
        var scriptsDir = PythonService.GetScriptsDirectory();
        if (string.IsNullOrEmpty(scriptsDir))
        {
            MessageBox.Show("Could not find training scripts directory.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var pipelineScript = Path.Combine(scriptsDir, "01_train_pipeline.py");
        if (!File.Exists(pipelineScript))
        {
            MessageBox.Show($"Pipeline script not found:\n{pipelineScript}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Determine training data path and model path based on mode
        string trainingDataPath;
        string? fineTuneModelPath = null;
        var gameModelsPath = Path.Combine(_configManager.GameModelsDirectory, _profile.GameId);

        if (mode == TrainingMode.FineTune)
        {
            trainingDataPath = NewTrainingDataPath;
            fineTuneModelPath = GetLatestPtModel();

            if (fineTuneModelPath == null || !File.Exists(fineTuneModelPath))
            {
                MessageBox.Show($"No .pt model found in:\n{gameModelsPath}\n\nPlease run a full training first.",
                    "Model Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            trainingDataPath = BaseTrainingDataPath;
        }

        // Build command line arguments
        var args = new List<string>
        {
            "--auto-yes",
            $"--game-id \"{_profile.GameId}\"",
            $"--model-name \"{_profile.GameId}_model\"",
            $"--training-data-path \"{trainingDataPath}\"",
            $"--game-models-path \"{gameModelsPath}\"",
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

        var argsString = string.Join(" ", args);
        var command = $"python \"{pipelineScript}\" {argsString}";

        try
        {
            // Launch visible terminal with /k to keep it open after command completes
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {command}",
                UseShellExecute = true,
                WorkingDirectory = scriptsDir
            };

            Process.Start(psi);

            MessageBox.Show(
                $"Training started in a new terminal window.\n\n" +
                $"Mode: {(mode == TrainingMode.FineTune ? "Fine-tune" : "Full Retrain")}\n" +
                $"Epochs: {trainingParams.Epochs}\n" +
                $"Image Size: {trainingParams.ImageSize}\n\n" +
                "The terminal will stay open when training completes.\n" +
                "Check the output for any errors.",
                "Training Started",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start training: {ex.Message}");
            MessageBox.Show($"Failed to start training:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pythonService.Dispose();
    }
}
