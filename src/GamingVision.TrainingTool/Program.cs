using GamingVision.Models;
using GamingVision.Services.Detection;
using GamingVision.Services.ScreenCapture;

namespace GamingVision.TrainingTool;

class Program
{
    private static readonly string TrainingDataRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "training_data");

    private static readonly string GameModelsRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "GameModels");

    static async Task Main(string[] args)
    {
        Console.Title = "GamingVision Training Data Tool";

        while (true)
        {
            Console.Clear();
            PrintHeader();

            var profiles = LoadGameProfiles();
            var selectedProfile = ShowMainMenu(profiles);

            if (selectedProfile == null)
            {
                // User chose to quit
                Console.WriteLine("\nGoodbye!");
                break;
            }

            await RunCaptureSession(selectedProfile);
        }
    }

    static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           GamingVision Training Data Tool                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    static List<GameProfile> LoadGameProfiles()
    {
        var profiles = new List<GameProfile>();

        if (!Directory.Exists(GameModelsRoot))
        {
            return profiles;
        }

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        foreach (var gameDir in Directory.GetDirectories(GameModelsRoot))
        {
            var configPath = Path.Combine(gameDir, "game_config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var profile = System.Text.Json.JsonSerializer.Deserialize<GameProfile>(json, jsonOptions);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not load profile from {gameDir}: {ex.Message}");
                }
            }
        }

        return profiles;
    }

    static GameProfile? ShowMainMenu(List<GameProfile> profiles)
    {
        Console.WriteLine("Available Games:");
        Console.WriteLine();

        int index = 1;
        foreach (var profile in profiles)
        {
            var modelPath = Path.Combine(GameModelsRoot, profile.GameId, profile.ModelFile);
            bool hasModel = File.Exists(modelPath);

            Console.Write($"  [{index}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(profile.DisplayName);
            Console.ResetColor();

            if (hasModel)
            {
                // Try to get label count
                var labelsPath = Path.ChangeExtension(modelPath, ".txt");
                int labelCount = 0;
                if (File.Exists(labelsPath))
                {
                    labelCount = File.ReadAllLines(labelsPath).Count(l => !string.IsNullOrWhiteSpace(l));
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" ({profile.ModelFile} - {labelCount} labels)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" (no model - screenshots only)");
            }
            Console.ResetColor();

            index++;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  [N] Create New Game Profile");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  [Q] Quit");
        Console.ResetColor();

        Console.WriteLine();
        Console.Write("Select option: ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim().ToUpper();

            if (input == "Q")
            {
                return null;
            }

            if (input == "N")
            {
                var newProfile = CreateNewGameProfile();
                if (newProfile != null)
                {
                    return newProfile;
                }
                // User cancelled, show menu again
                Console.Clear();
                PrintHeader();
                return ShowMainMenu(LoadGameProfiles());
            }

            if (int.TryParse(input, out int selection) && selection >= 1 && selection <= profiles.Count)
            {
                return profiles[selection - 1];
            }

            Console.Write("Invalid selection. Try again: ");
        }
    }

    static GameProfile? CreateNewGameProfile()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("═══ Create New Game Profile ═══");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("Enter game ID (folder name, no spaces): ");
        var gameId = Console.ReadLine()?.Trim().ToLower().Replace(" ", "_");

        if (string.IsNullOrWhiteSpace(gameId))
        {
            Console.WriteLine("Cancelled.");
            return null;
        }

        // Check if already exists
        var gameDir = Path.Combine(GameModelsRoot, gameId);
        if (Directory.Exists(gameDir))
        {
            Console.WriteLine($"Game profile '{gameId}' already exists.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return null;
        }

        Console.Write("Enter display name: ");
        var displayName = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = gameId;
        }

        Console.Write("Enter window title (for future use, can be empty): ");
        var windowTitle = Console.ReadLine()?.Trim() ?? "";

        // Create directories
        Directory.CreateDirectory(gameDir);

        var trainingDataManager = new TrainingDataManager(
            Path.GetFullPath(TrainingDataRoot), gameId);
        trainingDataManager.EnsureDirectories();

        // Create a minimal game_config.json
        var profile = new GameProfile
        {
            GameId = gameId,
            DisplayName = displayName,
            ModelFile = $"{gameId}_model.onnx",
            WindowTitle = windowTitle,
            PrimaryLabels = new List<string>(),
            SecondaryLabels = new List<string>(),
            TertiaryLabels = new List<string>(),
            LabelPriority = new List<string>()
        };

        // Save the profile
        var configPath = Path.Combine(gameDir, "game_config.json");
        var json = System.Text.Json.JsonSerializer.Serialize(profile, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(configPath, json);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Created: GameModels/{gameId}/");
        Console.WriteLine($"Created: training_data/{gameId}/images/");
        Console.WriteLine($"Created: training_data/{gameId}/labels/");
        Console.ResetColor();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Note: No model exists yet. Screenshots will be saved without annotations.");
        Console.WriteLine("You can label them using LabelImg.");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);

        return profile;
    }

    static async Task RunCaptureSession(GameProfile profile)
    {
        Console.Clear();

        var modelPath = Path.Combine(GameModelsRoot, profile.GameId, profile.ModelFile);
        bool hasModel = File.Exists(modelPath);

        // Initialize training data manager
        var trainingDataManager = new TrainingDataManager(
            Path.GetFullPath(TrainingDataRoot), profile.GameId);
        trainingDataManager.Initialize();

        // Get current stats
        var (imageCount, labeledCount) = trainingDataManager.GetStatistics();

        // Initialize detection service if model exists
        YoloDetectionService? detectionService = null;
        IReadOnlyList<string> labels = Array.Empty<string>();

        if (hasModel)
        {
            Console.WriteLine("Loading model...");
            detectionService = new YoloDetectionService();
            bool initialized = await detectionService.InitializeAsync(modelPath, useGpu: true);

            if (initialized)
            {
                labels = detectionService.Labels;

                // Save classes.txt for LabelImg
                if (labels.Count > 0)
                {
                    trainingDataManager.SaveClasses(labels);
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: Model failed to load. Screenshots will be saved without annotations.");
                Console.ResetColor();
                detectionService?.Dispose();
                detectionService = null;
            }
        }

        // Initialize screen capture (fullscreen, primary monitor)
        var captureService = new GdiCaptureService();
        captureService.InitializeForMonitor(0);

        // Print session info
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("════════════════════════════════════════════════════════════════");
        Console.ResetColor();

        Console.Write("Game: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(profile.DisplayName);
        Console.ResetColor();

        Console.Write("Model: ");
        if (detectionService != null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Loaded ({detectionService.ExecutionProvider}) - {labels.Count} labels");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Not available (screenshots only)");
        }
        Console.ResetColor();

        Console.Write("Output: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"training_data/{profile.GameId}/");
        Console.ResetColor();

        Console.Write("Existing: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{imageCount} images, {labeledCount} labeled");
        Console.ResetColor();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Controls:");
        Console.ResetColor();
        Console.WriteLine("  F1     - Capture screenshot");
        Console.WriteLine("  Escape - Back to menu");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("════════════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();

        // Capture state - use array to allow modification in lambda
        int[] captureCount = { 0 };
        bool sessionActive = true;
        object consoleLock = new();

        // Set up hotkey service
        using var hotkeyService = new ConsoleHotkeyService();

        hotkeyService.F1Pressed += async () =>
        {
            var count = await CaptureScreenshot(
                captureService,
                detectionService,
                trainingDataManager,
                labels,
                consoleLock);
            if (count > 0)
            {
                captureCount[0] += count;
            }
        };

        hotkeyService.EscapePressed += () =>
        {
            sessionActive = false;
        };

        hotkeyService.Start();

        // Print ready message
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[{timestamp}] Ready - Press F1 to capture...");
        Console.ResetColor();

        // Wait for escape
        while (sessionActive)
        {
            await Task.Delay(100);
        }

        // Cleanup
        hotkeyService.Stop();
        captureService.Dispose();
        detectionService?.Dispose();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Session ended. {captureCount[0]} screenshots captured.");
        Console.ResetColor();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    /// <summary>
    /// Captures a screenshot and returns 1 if successful, 0 otherwise.
    /// </summary>
    static async Task<int> CaptureScreenshot(
        GdiCaptureService captureService,
        YoloDetectionService? detectionService,
        TrainingDataManager trainingDataManager,
        IReadOnlyList<string> labels,
        object consoleLock)
    {
        try
        {
            // Capture the screen
            var frame = captureService.CaptureFrame();
            if (frame == null)
            {
                lock (consoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: Failed to capture screen");
                    Console.ResetColor();
                }
                return 0;
            }

            try
            {
                var filename = trainingDataManager.GetNextFilename();
                List<DetectedObject> detections = new();

                // Run detection if model available
                if (detectionService != null && detectionService.IsReady)
                {
                    detections = await detectionService.DetectAsync(frame, 0.3f);
                }

                // Save screenshot
                trainingDataManager.SaveScreenshot(frame, filename);

                // Save annotations only if detections found
                if (detections.Count > 0)
                {
                    trainingDataManager.SaveAnnotations(detections, filename, frame.Width, frame.Height, labels);
                }

                // Log result
                lock (consoleLock)
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    Console.Write($"[{timestamp}] Captured ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"{filename}.png");
                    Console.ResetColor();

                    if (detections.Count > 0)
                    {
                        Console.Write(" - ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($"{detections.Count} detections");
                        Console.ResetColor();

                        // Show unique labels found
                        var uniqueLabels = detections.Select(d => d.Label).Distinct().ToList();
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.Write($" ({string.Join(", ", uniqueLabels)})");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write(" - 0 detections (saved for manual labeling)");
                        Console.ResetColor();
                    }

                    Console.WriteLine();
                }

                return 1; // Success
            }
            finally
            {
                frame.Dispose();
            }
        }
        catch (Exception ex)
        {
            lock (consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
                Console.ResetColor();
            }
            return 0;
        }
    }
}
