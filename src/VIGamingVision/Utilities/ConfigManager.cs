using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VIGamingVision.Models;

namespace VIGamingVision.Utilities;

/// <summary>
/// Manages loading and saving of application configuration.
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

    private readonly string _configPath;

    /// <summary>
    /// Creates a new ConfigManager with the specified config file path.
    /// </summary>
    /// <param name="configPath">Path to the config.json file.</param>
    public ConfigManager(string? configPath = null)
    {
        _configPath = configPath ?? GetDefaultConfigPath();
    }

    /// <summary>
    /// Gets the default config file path (next to the executable).
    /// </summary>
    public static string GetDefaultConfigPath()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(exeDir, "config.json");
    }

    /// <summary>
    /// Loads the configuration from disk, or creates a default one if it doesn't exist.
    /// </summary>
    public async Task<AppConfiguration> LoadAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                var defaultConfig = AppConfiguration.CreateDefault();
                await SaveAsync(defaultConfig);
                return defaultConfig;
            }

            var json = await File.ReadAllTextAsync(_configPath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);

            if (config == null)
            {
                return AppConfiguration.CreateDefault();
            }

            // Ensure we have at least one game profile
            if (config.Games.Count == 0)
            {
                var defaultConfig = AppConfiguration.CreateDefault();
                config.Games = defaultConfig.Games;
                config.SelectedGame = defaultConfig.SelectedGame;
            }

            return config;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            return AppConfiguration.CreateDefault();
        }
    }

    /// <summary>
    /// Loads the configuration synchronously.
    /// </summary>
    public AppConfiguration Load()
    {
        return LoadAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Saves the configuration to disk.
    /// </summary>
    public async Task SaveAsync(AppConfiguration config)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Saves the configuration synchronously.
    /// </summary>
    public void Save(AppConfiguration config)
    {
        SaveAsync(config).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Checks if the config file exists.
    /// </summary>
    public bool ConfigExists() => File.Exists(_configPath);

    /// <summary>
    /// Gets the config file path.
    /// </summary>
    public string ConfigPath => _configPath;
}
