using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamingVision.Models;

namespace GamingVision.Utilities;

/// <summary>
/// Manages loading and saving of application and game configuration.
/// App settings are stored in app_settings.json.
/// Game settings are stored in GameModels/{gameId}/game_config.json.
/// </summary>
public class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _baseDirectory;
    private readonly string _appSettingsPath;
    private readonly string _gameModelsDirectory;

    /// <summary>
    /// Gets all loaded game profiles keyed by gameId.
    /// </summary>
    public Dictionary<string, GameProfile> GameProfiles { get; private set; } = [];

    /// <summary>
    /// Creates a new ConfigManager using the application base directory.
    /// </summary>
    public ConfigManager(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        _appSettingsPath = Path.Combine(_baseDirectory, "app_settings.json");
        _gameModelsDirectory = Path.Combine(_baseDirectory, "GameModels");
    }

    /// <summary>
    /// Loads the application configuration from app_settings.json.
    /// </summary>
    public async Task<AppConfiguration> LoadAppSettingsAsync()
    {
        try
        {
            if (!File.Exists(_appSettingsPath))
            {
                var defaultConfig = AppConfiguration.CreateDefault();
                await SaveAppSettingsAsync(defaultConfig);
                return defaultConfig;
            }

            var json = await File.ReadAllTextAsync(_appSettingsPath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);

            return config ?? AppConfiguration.CreateDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading app settings: {ex.Message}");
            return AppConfiguration.CreateDefault();
        }
    }

    /// <summary>
    /// Loads app settings synchronously.
    /// </summary>
    public AppConfiguration LoadAppSettings()
    {
        return LoadAppSettingsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Saves the application configuration to app_settings.json.
    /// </summary>
    public async Task SaveAppSettingsAsync(AppConfiguration config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_appSettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving app settings: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Discovers and loads all game profiles from GameModels subdirectories.
    /// </summary>
    public async Task<Dictionary<string, GameProfile>> LoadAllGameProfilesAsync()
    {
        GameProfiles.Clear();

        if (!Directory.Exists(_gameModelsDirectory))
        {
            System.Diagnostics.Debug.WriteLine($"GameModels directory not found: {_gameModelsDirectory}");
            return GameProfiles;
        }

        foreach (var gameDir in Directory.GetDirectories(_gameModelsDirectory))
        {
            var gameId = Path.GetFileName(gameDir);
            var configPath = Path.Combine(gameDir, "game_config.json");

            if (File.Exists(configPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    var profile = JsonSerializer.Deserialize<GameProfile>(json, JsonOptions);

                    if (profile != null)
                    {
                        // Ensure gameId matches folder name
                        profile.GameId = gameId;
                        GameProfiles[gameId] = profile;
                        System.Diagnostics.Debug.WriteLine($"Loaded game profile: {gameId} ({profile.DisplayName})");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading game profile {gameId}: {ex.Message}");
                }
            }
        }

        return GameProfiles;
    }

    /// <summary>
    /// Loads all game profiles synchronously.
    /// </summary>
    public Dictionary<string, GameProfile> LoadAllGameProfiles()
    {
        return LoadAllGameProfilesAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Loads a specific game profile by gameId.
    /// </summary>
    public async Task<GameProfile?> LoadGameProfileAsync(string gameId)
    {
        var configPath = Path.Combine(_gameModelsDirectory, gameId, "game_config.json");

        if (!File.Exists(configPath))
        {
            System.Diagnostics.Debug.WriteLine($"Game config not found: {configPath}");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var profile = JsonSerializer.Deserialize<GameProfile>(json, JsonOptions);

            if (profile != null)
            {
                profile.GameId = gameId;
                GameProfiles[gameId] = profile;
            }

            return profile;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading game profile {gameId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves a game profile to its game_config.json file.
    /// </summary>
    public async Task SaveGameProfileAsync(GameProfile profile)
    {
        if (string.IsNullOrEmpty(profile.GameId))
        {
            throw new ArgumentException("GameProfile must have a GameId");
        }

        var gameDir = Path.Combine(_gameModelsDirectory, profile.GameId);
        var configPath = Path.Combine(gameDir, "game_config.json");

        try
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(gameDir))
            {
                Directory.CreateDirectory(gameDir);
            }

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            await File.WriteAllTextAsync(configPath, json);

            // Update cache
            GameProfiles[profile.GameId] = profile;

            System.Diagnostics.Debug.WriteLine($"Saved game profile: {profile.GameId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving game profile {profile.GameId}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Saves a game profile synchronously.
    /// </summary>
    public void SaveGameProfile(GameProfile profile)
    {
        SaveGameProfileAsync(profile).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Deletes a game profile and its directory.
    /// </summary>
    public async Task DeleteGameProfileAsync(string gameId)
    {
        var gameDir = Path.Combine(_gameModelsDirectory, gameId);

        if (Directory.Exists(gameDir))
        {
            try
            {
                // Only delete the config file, not the model
                var configPath = Path.Combine(gameDir, "game_config.json");
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }

                GameProfiles.Remove(gameId);
                System.Diagnostics.Debug.WriteLine($"Deleted game profile: {gameId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting game profile {gameId}: {ex.Message}");
                throw;
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets a game profile by gameId from the cached profiles.
    /// </summary>
    public GameProfile? GetGameProfile(string gameId)
    {
        return GameProfiles.TryGetValue(gameId, out var profile) ? profile : null;
    }

    /// <summary>
    /// Gets the path to a game's model file.
    /// </summary>
    public string GetModelPath(string gameId, string modelFile)
    {
        return Path.Combine(_gameModelsDirectory, gameId, modelFile);
    }

    /// <summary>
    /// Gets the GameModels directory path.
    /// </summary>
    public string GameModelsDirectory => _gameModelsDirectory;

    /// <summary>
    /// Gets the app settings file path.
    /// </summary>
    public string AppSettingsPath => _appSettingsPath;
}
